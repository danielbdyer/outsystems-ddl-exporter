using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Domain.Abstractions;

namespace Osm.Validation.Tightening;

public sealed class TighteningPolicy
{
    public static PolicyResult<TighteningDecisions> Evaluate(OsmModel model, ProfileSnapshot snapshot, TighteningMode mode)
    {
        var options = CreateKernelOptions(mode);
        var policy = new TighteningPolicy();
        var decisionResult = policy.Decide(model, snapshot, options);

        return decisionResult.Kind switch
        {
            PolicyResultKind.Decision => PolicyResult<TighteningDecisions>.FromDecision(
                TighteningDecisions.FromPolicyDecisions(decisionResult.Decision),
                decisionResult.Warnings),
            PolicyResultKind.Warning => PolicyResult<TighteningDecisions>.FromWarnings(decisionResult.Warnings),
            PolicyResultKind.Error => PolicyResult<TighteningDecisions>.FromError(decisionResult.Error!),
            _ => PolicyResult<TighteningDecisions>.FromError(
                PolicyError.Create("policy.result.unexpected", "Unexpected policy evaluation outcome."))
        };
    }

    public PolicyResult<PolicyDecisionSet> Decide(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
    {
        var contextResult = PolicyContext.Create(model, snapshot, options);
        if (contextResult.IsFailure)
        {
            return PolicyResult<PolicyDecisionSet>.FromError(ToPolicyError(contextResult.Errors));
        }

        return Decide(contextResult.Value);
    }

    public PolicyResult<PolicyDecisionSet> Decide(PolicyContext context)
    {
        if (context is null)
        {
            return PolicyResult<PolicyDecisionSet>.FromError(
                PolicyError.Create("policy.context.null", "Policy context cannot be null."));
        }

        var warningBuilder = ImmutableArray.CreateBuilder<PolicyWarning>();
        var decisionSet = ComputeDecisionSet(context, warningBuilder);
        return PolicyResult<PolicyDecisionSet>.FromDecision(decisionSet, warningBuilder.ToImmutable());
    }

    private static PolicyError ToPolicyError(ImmutableArray<ValidationError> errors)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return PolicyError.Create("policy.context.invalid", "Policy context failed validation.");
        }

        var error = errors[0];
        return PolicyError.Create(error.Code, error.Message);
    }

    private static PolicyDecisionSet ComputeDecisionSet(
        PolicyContext context,
        ImmutableArray<PolicyWarning>.Builder warnings)
    {
        var model = context.Model;
        var snapshot = context.Profile;
        var options = context.Options;

        var columnProfiles = snapshot.Columns.ToDictionary(ColumnCoordinate.From, static c => c);
        var uniqueProfiles = snapshot.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static u => u);
        var fkReality = snapshot.ForeignKeys.ToDictionary(f => ColumnCoordinate.From(f.Reference), static f => f);
        var lookupResolution = BuildEntityLookup(model, options.Emission.NamingOverrides);
        var entityLookup = lookupResolution.Lookup;
        var attributeIndex = EntityAttributeIndex.Create(model);
        var foreignKeyTargets = ForeignKeyTargetIndex.Create(attributeIndex, entityLookup);

        var uniqueEvidence = UniqueIndexEvidenceAggregator.Create(
            model,
            uniqueProfiles,
            snapshot.CompositeUniqueCandidates,
            options.Uniqueness.EnforceSingleColumnUnique,
            options.Uniqueness.EnforceMultiColumnUnique);

        var singleUniqueClean = uniqueEvidence.SingleColumnClean;
        var singleUniqueDuplicates = uniqueEvidence.SingleColumnDuplicates;
        var compositeUniqueClean = uniqueEvidence.CompositeClean;
        var compositeUniqueDuplicates = uniqueEvidence.CompositeDuplicates;

        var uniqueStrategy = new UniqueIndexDecisionStrategy(options, columnProfiles, uniqueProfiles, uniqueEvidence);

        var missingColumnEvidence = new HashSet<ColumnCoordinate>();
        var missingForeignKeyEvidence = new HashSet<ColumnCoordinate>();

        var nullabilityBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, PolicyDecision<NullabilityDecision>>();
        var foreignKeyBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, PolicyDecision<ForeignKeyDecision>>();
        var uniqueIndexBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, PolicyDecision<UniqueIndexDecision>>();

        foreach (var entity in model.Modules.SelectMany(static m => m.Entities))
        {
            foreach (var attribute in attributeIndex.GetAttributes(entity))
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);

                columnProfiles.TryGetValue(coordinate, out var columnProfile);
                uniqueProfiles.TryGetValue(coordinate, out var uniqueProfile);
                fkReality.TryGetValue(coordinate, out var fkProfile);
                var foreignKeyTarget = foreignKeyTargets.GetTarget(coordinate);

                if (columnProfile is null)
                {
                    RecordMissingEvidence(
                        coordinate,
                        "profiling.column",
                        "policy.evidence.columnProfile.missing",
                        $"Column profiling metrics are unavailable for {coordinate}.",
                        warnings,
                        missingColumnEvidence);
                }

                if (attribute.Reference.IsReference && fkProfile is null)
                {
                    RecordMissingEvidence(
                        coordinate,
                        "profiling.foreignKey",
                        "policy.evidence.foreignKey.missing",
                        $"Foreign key profiling metrics are unavailable for {coordinate}.",
                        warnings,
                        missingForeignKeyEvidence);
                }

                var nullability = EvaluateNullability(
                    entity,
                    attribute,
                    coordinate,
                    columnProfile,
                    uniqueProfile,
                    fkProfile,
                    foreignKeyTarget,
                    options,
                    singleUniqueClean,
                    singleUniqueDuplicates,
                    compositeUniqueClean,
                    compositeUniqueDuplicates);

                var nullabilityEvidence = BuildColumnEvidence(coordinate, columnProfile, uniqueProfile, fkProfile);
                var nullabilityRemediation = BuildNullabilityRemediation(nullability);
                nullabilityBuilder[coordinate] = PolicyDecision<NullabilityDecision>.Create(
                    "policy.nullability",
                    nullability,
                    nullabilityEvidence,
                    nullabilityRemediation);

                if (attribute.Reference.IsReference)
                {
                    var fkDecision = EvaluateForeignKey(
                        entity,
                        attribute,
                        coordinate,
                        fkProfile,
                        options,
                        foreignKeyTarget);

                    var fkEvidence = BuildForeignKeyEvidence(coordinate, columnProfile, fkProfile);
                    var fkRemediation = BuildForeignKeyRemediation(coordinate, fkDecision, foreignKeyTarget);
                    foreignKeyBuilder[coordinate] = PolicyDecision<ForeignKeyDecision>.Create(
                        "policy.foreignKey",
                        fkDecision,
                        fkEvidence,
                        fkRemediation);
                }
            }

            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var indexCoordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
                var uniqueDecision = uniqueStrategy.Decide(entity, index);
                var uniqueEvidenceLinks = BuildUniqueIndexEvidence(
                    entity,
                    index,
                    columnProfiles,
                    warnings,
                    missingColumnEvidence);
                var uniqueRemediation = BuildUniqueIndexRemediation(entity, index, uniqueDecision);

                uniqueIndexBuilder[indexCoordinate] = PolicyDecision<UniqueIndexDecision>.Create(
                    "policy.uniqueIndex",
                    uniqueDecision,
                    uniqueEvidenceLinks,
                    uniqueRemediation);
            }
        }

        return PolicyDecisionSet.Create(
            nullabilityBuilder.ToImmutable(),
            foreignKeyBuilder.ToImmutable(),
            uniqueIndexBuilder.ToImmutable(),
            lookupResolution.Diagnostics);
    }

    private static IEnumerable<PolicyEvidenceLink> BuildColumnEvidence(
        ColumnCoordinate coordinate,
        ColumnProfile? columnProfile,
        UniqueCandidateProfile? uniqueProfile,
        ForeignKeyReality? fkProfile)
    {
        if (columnProfile is not null)
        {
            yield return PolicyEvidenceLink.FromColumnProfile(columnProfile);
        }

        if (uniqueProfile is not null)
        {
            yield return PolicyEvidenceLink.FromUniqueCandidate(uniqueProfile);
        }

        if (fkProfile is not null)
        {
            yield return PolicyEvidenceLink.FromForeignKeyReality(fkProfile);
        }
    }

    private static IEnumerable<PolicyEvidenceLink> BuildForeignKeyEvidence(
        ColumnCoordinate coordinate,
        ColumnProfile? columnProfile,
        ForeignKeyReality? fkProfile)
    {
        if (columnProfile is not null)
        {
            yield return PolicyEvidenceLink.FromColumnProfile(columnProfile);
        }

        if (fkProfile is not null)
        {
            yield return PolicyEvidenceLink.FromForeignKeyReality(fkProfile);
        }
        else
        {
            yield return PolicyEvidenceLink.Missing("profiling.foreignKey", coordinate.ToString());
        }
    }

    private static IEnumerable<PolicyEvidenceLink> BuildUniqueIndexEvidence(
        EntityModel entity,
        IndexModel index,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        ImmutableArray<PolicyWarning>.Builder warnings,
        ISet<ColumnCoordinate> missingColumnEvidence)
    {
        var orderedColumns = index.Columns
            .OrderBy(static column => column.Ordinal)
            .Select(column => new ColumnCoordinate(entity.Schema, entity.PhysicalName, column.Column));

        foreach (var coordinate in orderedColumns)
        {
            if (columnProfiles.TryGetValue(coordinate, out var profile))
            {
                yield return PolicyEvidenceLink.FromColumnProfile(profile);
            }
            else
            {
                RecordMissingEvidence(
                    coordinate,
                    "profiling.column",
                    "policy.evidence.columnProfile.missing",
                    $"Column profiling metrics are unavailable for {coordinate} while evaluating unique index {index.Name.Value}.",
                    warnings,
                    missingColumnEvidence);

                yield return PolicyEvidenceLink.Missing("profiling.column", coordinate.ToString());
            }
        }
    }

    private static IEnumerable<string> BuildNullabilityRemediation(NullabilityDecision decision)
    {
        if (!decision.RequiresRemediation)
        {
            return Array.Empty<string>();
        }

        var column = decision.Column;
        var table = $"[{column.Schema.Value}].[{column.Table.Value}]";
        var columnName = $"[{column.Column.Value}]";

        return new[]
        {
            $"UPDATE {table} SET {columnName} = <replacement_value> WHERE {columnName} IS NULL;"
        };
    }

    private static IEnumerable<string> BuildUniqueIndexRemediation(
        EntityModel entity,
        IndexModel index,
        UniqueIndexDecision decision)
    {
        if (!decision.RequiresRemediation)
        {
            return Array.Empty<string>();
        }

        var orderedColumns = index.Columns
            .OrderBy(static column => column.Ordinal)
            .Select(column => $"[{column.Column.Value}]");
        var columnList = string.Join(", ", orderedColumns);
        var table = $"[{entity.Schema.Value}].[{entity.PhysicalName.Value}]";

        return new[]
        {
            $"SELECT {columnList}, COUNT(*) AS DuplicateCount FROM {table} GROUP BY {columnList} HAVING COUNT(*) > 1;"
        };
    }

    private static IEnumerable<string> BuildForeignKeyRemediation(
        ColumnCoordinate coordinate,
        ForeignKeyDecision decision,
        EntityModel? foreignKeyTarget)
    {
        if (!decision.Rationales.Contains(TighteningRationales.DataHasOrphans))
        {
            return Array.Empty<string>();
        }

        if (foreignKeyTarget is null)
        {
            return new[]
            {
                $"/* Resolve orphan rows in [{coordinate.Schema.Value}].[{coordinate.Table.Value}] column [{coordinate.Column.Value}] before enforcing the foreign key. */"
            };
        }

        var sourceTable = $"[{coordinate.Schema.Value}].[{coordinate.Table.Value}]";
        var sourceColumn = $"[{coordinate.Column.Value}]";
        var targetTable = $"[{foreignKeyTarget.Schema.Value}].[{foreignKeyTarget.PhysicalName.Value}]";

        return new[]
        {
            $"SELECT {sourceColumn} FROM {sourceTable} src WHERE {sourceColumn} IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {targetTable} tgt WHERE tgt.{sourceColumn} = src.{sourceColumn});"
        };
    }

    private static void RecordMissingEvidence(
        ColumnCoordinate coordinate,
        string source,
        string code,
        string message,
        ImmutableArray<PolicyWarning>.Builder warnings,
        ISet<ColumnCoordinate> recorded)
    {
        if (!recorded.Add(coordinate))
        {
            return;
        }

        warnings.Add(PolicyWarning.Create(code, message, new[] { PolicyEvidenceLink.Missing(source, coordinate.ToString()) }));
    }

    private static TighteningOptions CreateKernelOptions(TighteningMode mode)
    {
        var defaults = TighteningOptions.Default;
        var policy = PolicyOptions.Create(mode, defaults.Policy.NullBudget).Value;

        return TighteningOptions.Create(
            policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            defaults.Emission,
            defaults.Mocking).Value;
    }

    private static NullabilityDecision EvaluateNullability(
        EntityModel entity,
        AttributeModel attribute,
        ColumnCoordinate coordinate,
        ColumnProfile? columnProfile,
        UniqueCandidateProfile? uniqueProfile,
        ForeignKeyReality? fkReality,
        EntityModel? foreignKeyTarget,
        TighteningOptions options,
        ISet<ColumnCoordinate> singleUniqueColumns,
        ISet<ColumnCoordinate> singleUniqueDuplicates,
        ISet<ColumnCoordinate> compositeUniqueColumns,
        ISet<ColumnCoordinate> compositeUniqueDuplicates)
    {
        var rationales = new SortedSet<string>(StringComparer.Ordinal);
        var makeNotNull = false;
        var requiresRemediation = false;

        if (attribute.IsIdentifier)
        {
            makeNotNull = true;
            rationales.Add(TighteningRationales.PrimaryKey);
        }

        if (columnProfile is ColumnProfile physical && !physical.IsNullablePhysical)
        {
            makeNotNull = true;
            rationales.Add(TighteningRationales.PhysicalNotNull);
        }

        if (columnProfile is null)
        {
            rationales.Add(TighteningRationales.ProfileMissing);
        }

        var dataWithinBudget = false;
        var budgetUsed = false;
        if (columnProfile is ColumnProfile prof)
        {
            dataWithinBudget = IsWithinNullBudget(prof, options.Policy.NullBudget, out budgetUsed);
        }

        if (budgetUsed)
        {
            rationales.Add(TighteningRationales.NullBudgetEpsilon);
        }

        var singleUniqueSignal = singleUniqueColumns.Contains(coordinate);
        if (singleUniqueSignal)
        {
            rationales.Add(TighteningRationales.UniqueNoNulls);
        }
        else if (singleUniqueDuplicates.Contains(coordinate) || uniqueProfile?.HasDuplicate == true)
        {
            rationales.Add(TighteningRationales.UniqueDuplicatesPresent);
        }

        var compositeUniqueSignal = compositeUniqueColumns.Contains(coordinate);

        if (compositeUniqueSignal)
        {
            rationales.Add(TighteningRationales.CompositeUniqueNoNulls);
        }

        if (compositeUniqueDuplicates.Contains(coordinate))
        {
            rationales.Add(TighteningRationales.CompositeUniqueDuplicatesPresent);
        }

        var uniqueSignal = singleUniqueSignal || compositeUniqueSignal;

        var mandatorySignal = attribute.IsMandatory;
        if (mandatorySignal)
        {
            rationales.Add(TighteningRationales.Mandatory);
            if (!string.IsNullOrEmpty(attribute.DefaultValue))
            {
                rationales.Add(TighteningRationales.DefaultPresent);
            }
        }

        if (attribute.Reference.IsReference)
        {
            if (IsIgnoreRule(attribute.Reference.DeleteRuleCode))
            {
                rationales.Add(TighteningRationales.DeleteRuleIgnore);
            }

            if (fkReality?.HasOrphan == true)
            {
                rationales.Add(TighteningRationales.DataHasOrphans);
            }
        }

        var fkSupports = attribute.Reference.IsReference
            && fkReality is ForeignKeyReality fk
            && !fk.HasOrphan
            && !IsIgnoreRule(attribute.Reference.DeleteRuleCode)
            && ForeignKeySupportsTightening(entity, fk, options.ForeignKeys, foreignKeyTarget);

        if (fkSupports)
        {
            rationales.Add(TighteningRationales.ForeignKeyEnforced);
        }

        var conditionalSignal = uniqueSignal || mandatorySignal || fkSupports;

        if (conditionalSignal)
        {
            switch (options.Policy.Mode)
            {
                case TighteningMode.Cautious:
                    break;
                case TighteningMode.EvidenceGated:
                    if (dataWithinBudget && columnProfile is not null)
                    {
                        makeNotNull = true;
                        rationales.Add(TighteningRationales.DataNoNulls);
                    }

                    break;
                case TighteningMode.Aggressive:
                    if (dataWithinBudget && columnProfile is not null)
                    {
                        makeNotNull = true;
                        rationales.Add(TighteningRationales.DataNoNulls);
                    }
                    else
                    {
                        makeNotNull = true;
                        requiresRemediation = true;
                        rationales.Add(TighteningRationales.RemediateBeforeTighten);
                    }

                    break;
            }
        }

        return NullabilityDecision.Create(coordinate, makeNotNull, requiresRemediation, rationales.ToImmutableArray());
    }

    private static ForeignKeyDecision EvaluateForeignKey(
        EntityModel entity,
        AttributeModel attribute,
        ColumnCoordinate coordinate,
        ForeignKeyReality? fkReality,
        TighteningOptions options,
        EntityModel? targetEntity)
    {
        var rationales = new SortedSet<string>(StringComparer.Ordinal);
        var createConstraint = false;

        if (!attribute.Reference.IsReference)
        {
            return ForeignKeyDecision.Create(coordinate, createConstraint, rationales.ToImmutableArray());
        }

        var ignoreRule = IsIgnoreRule(attribute.Reference.DeleteRuleCode);
        if (ignoreRule)
        {
            rationales.Add(TighteningRationales.DeleteRuleIgnore);
        }

        var hasOrphan = fkReality?.HasOrphan ?? false;
        if (hasOrphan)
        {
            rationales.Add(TighteningRationales.DataHasOrphans);
        }

        var hasConstraint = fkReality?.Reference.HasDatabaseConstraint ?? false;
        if (hasConstraint)
        {
            createConstraint = true;
            rationales.Add(TighteningRationales.DatabaseConstraintPresent);
        }

        var crossSchema = targetEntity is not null && !SchemaEquals(entity.Schema, targetEntity.Schema);
        var crossCatalog = targetEntity is not null && !CatalogEquals(entity.Catalog, targetEntity.Catalog);

        var crossSchemaBlocked = crossSchema && !options.ForeignKeys.AllowCrossSchema && !hasConstraint;
        var crossCatalogBlocked = crossCatalog && !options.ForeignKeys.AllowCrossCatalog && !hasConstraint;

        if (!hasConstraint && !ignoreRule && !hasOrphan && !crossSchemaBlocked && !crossCatalogBlocked && options.ForeignKeys.EnableCreation)
        {
            createConstraint = true;
            rationales.Add(TighteningRationales.PolicyEnableCreation);
        }
        else
        {
            if (!options.ForeignKeys.EnableCreation && !hasConstraint && !ignoreRule && !hasOrphan)
            {
                rationales.Add(TighteningRationales.ForeignKeyCreationDisabled);
            }

            if (crossSchemaBlocked)
            {
                rationales.Add(TighteningRationales.CrossSchema);
            }

            if (crossCatalogBlocked)
            {
                rationales.Add(TighteningRationales.CrossCatalog);
            }
        }

        return ForeignKeyDecision.Create(coordinate, createConstraint, rationales.ToImmutableArray());
    }

    private static bool ForeignKeySupportsTightening(
        EntityModel entity,
        ForeignKeyReality fkReality,
        ForeignKeyOptions options,
        EntityModel? target)
    {
        if (fkReality.Reference.HasDatabaseConstraint)
        {
            return true;
        }

        if (!options.EnableCreation)
        {
            return false;
        }

        if (target is null)
        {
            return false;
        }

        if (!options.AllowCrossSchema && !SchemaEquals(entity.Schema, target.Schema))
        {
            return false;
        }

        if (!options.AllowCrossCatalog && !CatalogEquals(entity.Catalog, target.Catalog))
        {
            return false;
        }

        return true;
    }

    private static bool IsIgnoreRule(string? deleteRule)
        => string.IsNullOrWhiteSpace(deleteRule) || string.Equals(deleteRule, "Ignore", StringComparison.OrdinalIgnoreCase);

    private static bool SchemaEquals(SchemaName left, SchemaName right)
        => string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private static bool CatalogEquals(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static EntityLookupResolution BuildEntityLookup(OsmModel model, NamingOverrideOptions namingOverrides)
    {
        var lookup = new Dictionary<EntityName, EntityModel>();
        var diagnostics = ImmutableArray.CreateBuilder<TighteningDiagnostic>();

        var groups = model.Modules
            .SelectMany(static module => module.Entities, (module, entity) => new { Module = module, Entity = entity })
            .GroupBy(x => x.Entity.LogicalName);

        foreach (var group in groups)
        {
            var candidates = group
                .Select(x => new DuplicateCandidate(x.Module, x.Entity))
                .ToArray();

            if (candidates.Length == 0)
            {
                continue;
            }

            if (candidates.Length == 1)
            {
                lookup[group.Key] = candidates[0].Entity;
                continue;
            }

            var resolution = ResolveDuplicates(group.Key, candidates, namingOverrides);
            lookup[group.Key] = resolution.Canonical.Entity;
            diagnostics.Add(resolution.Diagnostic);
        }

        return new EntityLookupResolution(lookup, diagnostics.ToImmutable());
    }

    private static DuplicateResolution ResolveDuplicates(
        EntityName logicalName,
        IReadOnlyList<DuplicateCandidate> candidates,
        NamingOverrideOptions namingOverrides)
    {
        var moduleOverrideMatches = candidates
            .Where(candidate => namingOverrides.TryGetModuleScopedEntityOverride(
                candidate.Module.Name.Value,
                logicalName.Value,
                out _))
            .ToArray();

        DuplicateCandidate canonical;
        bool resolvedByOverride;
        string code;
        string message;
        TighteningDiagnosticSeverity severity;

        if (moduleOverrideMatches.Length == 1)
        {
            canonical = moduleOverrideMatches[0];
            resolvedByOverride = true;
            severity = TighteningDiagnosticSeverity.Info;
            code = "tightening.entity.duplicate.resolved";
            message = BuildResolvedDuplicateMessage(logicalName.Value, canonical.Module.Name.Value, candidates);
        }
        else
        {
            canonical = candidates
                .OrderBy(c => c.Module.Name.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Entity.Schema.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Entity.PhysicalName.Value, StringComparer.OrdinalIgnoreCase)
                .First();

            resolvedByOverride = false;
            severity = TighteningDiagnosticSeverity.Warning;
            code = moduleOverrideMatches.Length > 1
                ? "tightening.entity.duplicate.conflict"
                : "tightening.entity.duplicate.unresolved";
            message = BuildUnresolvedDuplicateMessage(
                logicalName.Value,
                canonical.Module.Name.Value,
                moduleOverrideMatches.Length > 1,
                candidates);
        }

        var candidateRecords = candidates
            .OrderBy(c => c.Module.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Entity.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Entity.PhysicalName.Value, StringComparer.OrdinalIgnoreCase)
            .Select(c => new TighteningDuplicateCandidate(
                c.Module.Name.Value,
                c.Entity.Schema.Value,
                c.Entity.PhysicalName.Value))
            .ToImmutableArray();

        var diagnostic = new TighteningDiagnostic(
            code,
            message,
            severity,
            logicalName.Value,
            canonical.Module.Name.Value,
            canonical.Entity.Schema.Value,
            canonical.Entity.PhysicalName.Value,
            candidateRecords,
            resolvedByOverride);

        return new DuplicateResolution(canonical, diagnostic);
    }

    private static string BuildResolvedDuplicateMessage(
        string logicalName,
        string canonicalModule,
        IReadOnlyList<DuplicateCandidate> candidates)
    {
        var modules = string.Join(", ", candidates.Select(c => c.Module.Name.Value).Distinct(StringComparer.OrdinalIgnoreCase));
        return $"Entity logical name '{logicalName}' appears in modules [{modules}]. Canonical module '{canonicalModule}' was selected via module-scoped naming override.";
    }

    private static string BuildUnresolvedDuplicateMessage(
        string logicalName,
        string canonicalModule,
        bool conflictingOverrides,
        IReadOnlyList<DuplicateCandidate> candidates)
    {
        var modules = string.Join(", ", candidates.Select(c => c.Module.Name.Value).Distinct(StringComparer.OrdinalIgnoreCase));
        if (conflictingOverrides)
        {
            return $"Entity logical name '{logicalName}' has multiple module-scoped naming overrides across modules [{modules}]. Canonical module '{canonicalModule}' was selected by deterministic ordering.";
        }

        return $"Entity logical name '{logicalName}' appears in modules [{modules}] without a module-scoped naming override. Canonical module '{canonicalModule}' was selected by deterministic ordering.";
    }

    private sealed record DuplicateCandidate(ModuleModel Module, EntityModel Entity);

    private sealed record DuplicateResolution(DuplicateCandidate Canonical, TighteningDiagnostic Diagnostic);

    private sealed record EntityLookupResolution(
        IReadOnlyDictionary<EntityName, EntityModel> Lookup,
        ImmutableArray<TighteningDiagnostic> Diagnostics);

    private static bool IsWithinNullBudget(ColumnProfile profile, double nullBudget, out bool usedBudget)
    {
        usedBudget = false;

        if (profile.NullCount == 0)
        {
            return true;
        }

        if (profile.RowCount == 0)
        {
            return true;
        }

        if (nullBudget <= 0)
        {
            return false;
        }

        var allowed = profile.RowCount * nullBudget;
        if (profile.NullCount <= allowed)
        {
            usedBudget = true;
            return true;
        }

        return false;
    }
}
