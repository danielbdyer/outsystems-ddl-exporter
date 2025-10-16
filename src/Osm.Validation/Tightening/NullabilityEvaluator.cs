using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

internal sealed class NullabilityEvaluator
{
    private readonly TighteningOptions _options;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> _columnProfiles;
    private readonly IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> _uniqueProfiles;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> _foreignKeys;
    private readonly ForeignKeyTargetIndex _foreignKeyTargets;
    private readonly ISet<ColumnCoordinate> _singleUniqueClean;
    private readonly ISet<ColumnCoordinate> _singleUniqueDuplicates;
    private readonly ISet<ColumnCoordinate> _compositeUniqueClean;
    private readonly ISet<ColumnCoordinate> _compositeUniqueDuplicates;

    public NullabilityEvaluator(
        TighteningOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeys,
        ForeignKeyTargetIndex foreignKeyTargets,
        ISet<ColumnCoordinate> singleUniqueClean,
        ISet<ColumnCoordinate> singleUniqueDuplicates,
        ISet<ColumnCoordinate> compositeUniqueClean,
        ISet<ColumnCoordinate> compositeUniqueDuplicates)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _columnProfiles = columnProfiles ?? throw new ArgumentNullException(nameof(columnProfiles));
        _uniqueProfiles = uniqueProfiles ?? throw new ArgumentNullException(nameof(uniqueProfiles));
        _foreignKeys = foreignKeys ?? throw new ArgumentNullException(nameof(foreignKeys));
        _foreignKeyTargets = foreignKeyTargets ?? throw new ArgumentNullException(nameof(foreignKeyTargets));
        _singleUniqueClean = singleUniqueClean ?? throw new ArgumentNullException(nameof(singleUniqueClean));
        _singleUniqueDuplicates = singleUniqueDuplicates ?? throw new ArgumentNullException(nameof(singleUniqueDuplicates));
        _compositeUniqueClean = compositeUniqueClean ?? throw new ArgumentNullException(nameof(compositeUniqueClean));
        _compositeUniqueDuplicates = compositeUniqueDuplicates ?? throw new ArgumentNullException(nameof(compositeUniqueDuplicates));
    }

    public NullabilityDecision Evaluate(EntityModel entity, AttributeModel attribute, ColumnCoordinate coordinate)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var rationales = new SortedSet<string>(StringComparer.Ordinal);
        var makeNotNull = false;
        var requiresRemediation = false;

        if (attribute.IsIdentifier)
        {
            makeNotNull = true;
            rationales.Add(TighteningRationales.PrimaryKey);
        }

        var columnProfile = _columnProfiles.TryGetValue(coordinate, out var profile) ? profile : null;
        var uniqueProfile = _uniqueProfiles.TryGetValue(coordinate, out var uniqueCandidate) ? uniqueCandidate : null;
        var fkReality = _foreignKeys.TryGetValue(coordinate, out var fk) ? fk : null;
        var foreignKeyTarget = _foreignKeyTargets.GetTarget(coordinate);

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
            dataWithinBudget = IsWithinNullBudget(prof, _options.Policy.NullBudget, out budgetUsed);
        }

        if (budgetUsed)
        {
            rationales.Add(TighteningRationales.NullBudgetEpsilon);
        }

        var singleUniqueSignal = _singleUniqueClean.Contains(coordinate);
        if (singleUniqueSignal)
        {
            rationales.Add(TighteningRationales.UniqueNoNulls);
        }
        else if (_singleUniqueDuplicates.Contains(coordinate) || uniqueProfile?.HasDuplicate == true)
        {
            rationales.Add(TighteningRationales.UniqueDuplicatesPresent);
        }

        var compositeUniqueSignal = _compositeUniqueClean.Contains(coordinate);
        if (compositeUniqueSignal)
        {
            rationales.Add(TighteningRationales.CompositeUniqueNoNulls);
        }

        if (_compositeUniqueDuplicates.Contains(coordinate))
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
            && fkReality is ForeignKeyReality fkRealityProfile
            && !fkRealityProfile.HasOrphan
            && !IsIgnoreRule(attribute.Reference.DeleteRuleCode)
            && ForeignKeySupportsTightening(entity, fkRealityProfile, _options.ForeignKeys, foreignKeyTarget);

        if (fkSupports)
        {
            rationales.Add(TighteningRationales.ForeignKeyEnforced);
        }

        var conditionalSignal = uniqueSignal || mandatorySignal || fkSupports;

        if (conditionalSignal)
        {
            switch (_options.Policy.Mode)
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
}
