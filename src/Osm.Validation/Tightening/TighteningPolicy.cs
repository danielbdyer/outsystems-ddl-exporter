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
        var (compositeUniqueClean, compositeUniqueDuplicates) = BuildCompositeUniqueSignals(model, snapshot.CompositeUniqueCandidates, options.Uniqueness.EnforceMultiColumnUnique);

        var nullabilityBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, NullabilityDecision>();
        var foreignKeyBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyDecision>();

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
        }

        return PolicyDecisionSet.Create(nullabilityBuilder.ToImmutable(), foreignKeyBuilder.ToImmutable());
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

    private static (ISet<ColumnCoordinate> Clean, ISet<ColumnCoordinate> Duplicates) BuildCompositeUniqueSignals(
        OsmModel model,
        ImmutableArray<CompositeUniqueCandidateProfile> compositeProfiles,
        bool enforceComposite)
    {
        var clean = new HashSet<ColumnCoordinate>();
        var duplicates = new HashSet<ColumnCoordinate>();

        if (compositeProfiles.IsDefaultOrEmpty)
        {
            return (clean, duplicates);
        }

        var lookup = compositeProfiles.ToDictionary(
            profile => CreateCompositeKey(profile.Schema.Value, profile.Table.Value, profile.Columns.Select(c => c.Value)),
            profile => profile,
            StringComparer.OrdinalIgnoreCase);

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

        return (clean, duplicates);
    }

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
