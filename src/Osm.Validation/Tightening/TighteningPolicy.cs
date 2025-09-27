using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

public sealed class TighteningPolicy
{
    public PolicyDecisionSet Decide(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var columnProfiles = snapshot.Columns.ToDictionary(ColumnCoordinate.From, static c => c);
        var uniqueProfiles = snapshot.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static u => u);
        var fkReality = snapshot.ForeignKeys.ToDictionary(f => ColumnCoordinate.From(f.Reference), static f => f);
        var entityLookup = model.Modules
            .SelectMany(m => m.Entities)
            .ToDictionary(e => e.LogicalName, e => e);

        var singleUniqueClean = BuildSingleColumnUniqueSignals(model, uniqueProfiles, options.Uniqueness.EnforceSingleColumnUnique);
        var singleUniqueDuplicates = BuildSingleColumnDuplicateSignals(model, uniqueProfiles);
        var compositeSignals = BuildCompositeUniqueSignals(model, snapshot.CompositeUniqueCandidates, options.Uniqueness.EnforceMultiColumnUnique);
        var compositeUniqueClean = compositeSignals.Clean;
        var compositeUniqueDuplicates = compositeSignals.Duplicates;
        var compositeProfileLookup = compositeSignals.Lookup;

        var nullabilityBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, NullabilityDecision>();
        var foreignKeyBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyDecision>();
        var uniqueIndexBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, UniqueIndexDecision>();

        foreach (var entity in model.Modules.SelectMany(m => m.Entities))
        {
            foreach (var attribute in entity.Attributes)
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);

                ColumnProfile? columnProfile = columnProfiles.TryGetValue(coordinate, out var profile) ? profile : null;
                UniqueCandidateProfile? uniqueProfile = uniqueProfiles.TryGetValue(coordinate, out var unique) ? unique : null;
                ForeignKeyReality? fkProfile = fkReality.TryGetValue(coordinate, out var fk) ? fk : null;

                var nullability = EvaluateNullability(
                    entity,
                    attribute,
                    coordinate,
                    columnProfile,
                    uniqueProfile,
                    fkProfile,
                    options,
                    entityLookup,
                    singleUniqueClean,
                    singleUniqueDuplicates,
                    compositeUniqueClean,
                    compositeUniqueDuplicates);

                nullabilityBuilder[coordinate] = nullability;

                if (attribute.Reference.IsReference)
                {
                    var fkDecision = EvaluateForeignKey(
                        entity,
                        attribute,
                        coordinate,
                        fkProfile,
                        options,
                        entityLookup);

                    foreignKeyBuilder[coordinate] = fkDecision;
                }
            }

            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var indexCoordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
                var uniqueDecision = EvaluateUniqueIndex(
                    entity,
                    index,
                    indexCoordinate,
                    options,
                    columnProfiles,
                    uniqueProfiles,
                    compositeProfileLookup,
                    singleUniqueClean,
                    singleUniqueDuplicates,
                    compositeUniqueClean,
                    compositeUniqueDuplicates);

                uniqueIndexBuilder[indexCoordinate] = uniqueDecision;
            }
        }

        return PolicyDecisionSet.Create(
            nullabilityBuilder.ToImmutable(),
            foreignKeyBuilder.ToImmutable(),
            uniqueIndexBuilder.ToImmutable());
    }

    private static NullabilityDecision EvaluateNullability(
        EntityModel entity,
        AttributeModel attribute,
        ColumnCoordinate coordinate,
        ColumnProfile? columnProfile,
        UniqueCandidateProfile? uniqueProfile,
        ForeignKeyReality? fkReality,
        TighteningOptions options,
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup,
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
            && ForeignKeySupportsTightening(entity, attribute, fk, options.ForeignKeys, entityLookup);

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
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup)
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

        var targetEntity = GetTargetEntity(attribute.Reference, entityLookup);
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

    private static UniqueIndexDecision EvaluateUniqueIndex(
        EntityModel entity,
        IndexModel index,
        IndexCoordinate coordinate,
        TighteningOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> compositeProfileLookup,
        ISet<ColumnCoordinate> singleUniqueClean,
        ISet<ColumnCoordinate> singleUniqueDuplicates,
        ISet<ColumnCoordinate> compositeUniqueClean,
        ISet<ColumnCoordinate> compositeUniqueDuplicates)
    {
        var rationales = new SortedSet<string>(StringComparer.Ordinal);

        if (!index.IsUnique)
        {
            return UniqueIndexDecision.Create(coordinate, false, false, rationales.ToImmutableArray());
        }

        var columnCoordinates = index.Columns
            .Select(c => new ColumnCoordinate(entity.Schema, entity.PhysicalName, c.Column))
            .ToArray();

        var physicalUnique = columnCoordinates.All(c => columnProfiles.TryGetValue(c, out var profile) && profile.IsUniqueKey);
        if (physicalUnique)
        {
            rationales.Add(TighteningRationales.PhysicalUniqueKey);
        }

        var isComposite = index.Columns.Length > 1;
        var policyDisabled = isComposite
            ? !options.Uniqueness.EnforceMultiColumnUnique
            : !options.Uniqueness.EnforceSingleColumnUnique;

        if (policyDisabled)
        {
            rationales.Add(TighteningRationales.UniquePolicyDisabled);
        }

        var hasProfile = false;
        var hasDuplicates = false;
        var dataClean = false;
        var hasEvidence = false;

        if (isComposite)
        {
            var key = CreateCompositeKey(entity.Schema.Value, entity.PhysicalName.Value, index.Columns.Select(c => c.Column.Value));
            if (compositeProfileLookup.TryGetValue(key, out var profile))
            {
                hasProfile = true;
                hasEvidence = true;
                hasDuplicates = profile.HasDuplicate;
                dataClean = !profile.HasDuplicate;
            }
            else
            {
                if (columnCoordinates.Any(compositeUniqueDuplicates.Contains))
                {
                    hasEvidence = true;
                    hasDuplicates = true;
                }
                else if (columnCoordinates.All(compositeUniqueClean.Contains))
                {
                    hasEvidence = true;
                    dataClean = true;
                }
            }

            if (hasDuplicates)
            {
                rationales.Add(TighteningRationales.CompositeUniqueDuplicatesPresent);
            }
            else if (dataClean)
            {
                rationales.Add(TighteningRationales.CompositeUniqueNoNulls);
            }
        }
        else
        {
            var columnCoordinate = columnCoordinates[0];
            if (uniqueProfiles.TryGetValue(columnCoordinate, out var profile))
            {
                hasProfile = true;
                hasEvidence = true;
                hasDuplicates = profile.HasDuplicate;
                dataClean = !profile.HasDuplicate;
            }
            else if (singleUniqueDuplicates.Contains(columnCoordinate))
            {
                hasEvidence = true;
                hasDuplicates = true;
            }
            else if (singleUniqueClean.Contains(columnCoordinate))
            {
                hasEvidence = true;
                dataClean = true;
            }

            if (hasDuplicates)
            {
                rationales.Add(TighteningRationales.UniqueDuplicatesPresent);
            }
            else if (dataClean)
            {
                rationales.Add(TighteningRationales.UniqueNoNulls);
            }
        }

        if (!hasProfile && !physicalUnique)
        {
            rationales.Add(TighteningRationales.ProfileMissing);
        }

        var enforceUnique = false;
        var requiresRemediation = false;

        if (policyDisabled)
        {
            enforceUnique = false;
        }
        else if (hasDuplicates)
        {
            if (options.Policy.Mode == TighteningMode.Aggressive)
            {
                enforceUnique = true;
                requiresRemediation = true;
                rationales.Add(TighteningRationales.RemediateBeforeTighten);
            }
            else if (physicalUnique)
            {
                enforceUnique = true;
            }
            else
            {
                enforceUnique = false;
            }
        }
        else if (physicalUnique)
        {
            enforceUnique = true;
        }
        else
        {
            switch (options.Policy.Mode)
            {
                case TighteningMode.Cautious:
                    enforceUnique = false;
                    break;
                case TighteningMode.EvidenceGated:
                    enforceUnique = hasEvidence && dataClean;
                    break;
                case TighteningMode.Aggressive:
                    enforceUnique = true;
                    if (!hasEvidence || !dataClean)
                    {
                        requiresRemediation = true;
                        rationales.Add(TighteningRationales.RemediateBeforeTighten);
                    }

                    break;
            }
        }

        return UniqueIndexDecision.Create(coordinate, enforceUnique, requiresRemediation, rationales.ToImmutableArray());
    }

    private static ISet<ColumnCoordinate> BuildSingleColumnUniqueSignals(
        OsmModel model,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        bool enforceSingleColumnUnique)
    {
        var result = new HashSet<ColumnCoordinate>();

        if (!enforceSingleColumnUnique)
        {
            return result;
        }

        foreach (var entity in model.Modules.SelectMany(m => m.Entities))
        {
            foreach (var index in entity.Indexes.Where(i => i.IsUnique && i.Columns.Length == 1))
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, index.Columns[0].Column);
                if (uniqueProfiles.TryGetValue(coordinate, out var profile) && !profile.HasDuplicate)
                {
                    result.Add(coordinate);
                }
            }
        }

        return result;
    }

    private static ISet<ColumnCoordinate> BuildSingleColumnDuplicateSignals(
        OsmModel model,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles)
    {
        var result = new HashSet<ColumnCoordinate>();

        foreach (var entity in model.Modules.SelectMany(m => m.Entities))
        {
            foreach (var index in entity.Indexes.Where(i => i.IsUnique && i.Columns.Length == 1))
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, index.Columns[0].Column);
                if (uniqueProfiles.TryGetValue(coordinate, out var profile) && profile.HasDuplicate)
                {
                    result.Add(coordinate);
                }
            }
        }

        return result;
    }

    private static bool ForeignKeySupportsTightening(
        EntityModel entity,
        AttributeModel attribute,
        ForeignKeyReality fkReality,
        ForeignKeyOptions options,
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup)
    {
        if (fkReality.Reference.HasDatabaseConstraint)
        {
            return true;
        }

        if (!options.EnableCreation)
        {
            return false;
        }

        var target = GetTargetEntity(attribute.Reference, entityLookup);
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

    private static CompositeUniqueSignalSet BuildCompositeUniqueSignals(
        OsmModel model,
        ImmutableArray<CompositeUniqueCandidateProfile> compositeProfiles,
        bool enforceComposite)
    {
        var clean = new HashSet<ColumnCoordinate>();
        var duplicates = new HashSet<ColumnCoordinate>();
        var lookup = new Dictionary<string, CompositeUniqueCandidateProfile>(StringComparer.OrdinalIgnoreCase);

        if (!compositeProfiles.IsDefaultOrEmpty)
        {
            foreach (var profile in compositeProfiles)
            {
                var key = CreateCompositeKey(profile.Schema.Value, profile.Table.Value, profile.Columns.Select(c => c.Value));
                lookup[key] = profile;
            }
        }

        foreach (var entity in model.Modules.SelectMany(m => m.Entities))
        {
            foreach (var index in entity.Indexes.Where(i => i.IsUnique && i.Columns.Length > 1))
            {
                var key = CreateCompositeKey(entity.Schema.Value, entity.PhysicalName.Value, index.Columns.Select(c => c.Column.Value));
                if (!lookup.TryGetValue(key, out var profile))
                {
                    continue;
                }

                foreach (var column in index.Columns)
                {
                    var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, column.Column);
                    if (profile.HasDuplicate)
                    {
                        duplicates.Add(coordinate);
                    }
                    else if (enforceComposite)
                    {
                        clean.Add(coordinate);
                    }
                }
            }
        }

        return new CompositeUniqueSignalSet(clean, duplicates, lookup);
    }

    private sealed record CompositeUniqueSignalSet(
        ISet<ColumnCoordinate> Clean,
        ISet<ColumnCoordinate> Duplicates,
        IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> Lookup);

    private static string CreateCompositeKey(string schema, string table, IEnumerable<string> columns)
    {
        var normalizedColumns = columns
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c.Trim().ToUpperInvariant())
            .OrderBy(static c => c, StringComparer.Ordinal);

        return $"{schema.ToUpperInvariant()}|{table.ToUpperInvariant()}|{string.Join(',', normalizedColumns)}";
    }

    private static EntityModel? GetTargetEntity(AttributeReference reference, IReadOnlyDictionary<EntityName, EntityModel> lookup)
    {
        if (!reference.IsReference || reference.TargetEntity is null)
        {
            return null;
        }

        return lookup.TryGetValue(reference.TargetEntity.Value, out var entity) ? entity : null;
    }

    private static bool IsIgnoreRule(string? deleteRule)
        => string.IsNullOrWhiteSpace(deleteRule) || string.Equals(deleteRule, "Ignore", StringComparison.OrdinalIgnoreCase);

    private static bool SchemaEquals(SchemaName left, SchemaName right)
        => string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private static bool CatalogEquals(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

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
