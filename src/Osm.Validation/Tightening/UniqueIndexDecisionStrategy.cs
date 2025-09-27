using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

public sealed class UniqueIndexDecisionStrategy
{
    private readonly TighteningOptions _options;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> _columnProfiles;
    private readonly IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> _uniqueProfiles;
    private readonly UniqueIndexEvidenceAggregator _evidence;

    public UniqueIndexDecisionStrategy(
        TighteningOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        UniqueIndexEvidenceAggregator evidence)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _columnProfiles = columnProfiles ?? throw new ArgumentNullException(nameof(columnProfiles));
        _uniqueProfiles = uniqueProfiles ?? throw new ArgumentNullException(nameof(uniqueProfiles));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
    }

    public UniqueIndexDecision Decide(EntityModel entity, IndexModel index)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (index is null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        var coordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);

        if (!index.IsUnique)
        {
            return UniqueIndexDecision.Create(coordinate, false, false, ImmutableArray<string>.Empty);
        }

        var columnCoordinates = index.Columns
            .Select(c => new ColumnCoordinate(entity.Schema, entity.PhysicalName, c.Column))
            .ToArray();

        var physicalUnique = columnCoordinates.All(IsPhysicalUnique);
        var isComposite = index.Columns.Length > 1;
        var policyDisabled = isComposite
            ? !_options.Uniqueness.EnforceMultiColumnUnique
            : !_options.Uniqueness.EnforceSingleColumnUnique;

        var rationales = new SortedSet<string>(StringComparer.Ordinal);

        if (physicalUnique)
        {
            rationales.Add(TighteningRationales.PhysicalUniqueKey);
        }

        if (policyDisabled)
        {
            rationales.Add(TighteningRationales.UniquePolicyDisabled);
        }

        var evidence = isComposite
            ? EvaluateCompositeEvidence(entity, index, columnCoordinates, rationales)
            : EvaluateSingleColumnEvidence(columnCoordinates[0], rationales);

        if (!evidence.HasProfile && !physicalUnique)
        {
            rationales.Add(TighteningRationales.ProfileMissing);
        }

        var (enforceUnique, requiresRemediation) = DetermineOutcome(policyDisabled, physicalUnique, evidence);

        return UniqueIndexDecision.Create(coordinate, enforceUnique, requiresRemediation, rationales.ToImmutableArray());
    }

    private (bool EnforceUnique, bool RequiresRemediation) DetermineOutcome(
        bool policyDisabled,
        bool physicalUnique,
        UniqueIndexEvidence evidence)
    {
        if (policyDisabled)
        {
            return (false, false);
        }

        if (evidence.HasDuplicates)
        {
            if (_options.Policy.Mode == TighteningMode.Aggressive)
            {
                return (true, AddRemediation(evidence.Rationales));
            }

            if (physicalUnique)
            {
                return (true, false);
            }

            return (false, false);
        }

        if (physicalUnique)
        {
            return (true, false);
        }

        return _options.Policy.Mode switch
        {
            TighteningMode.Cautious => (false, false),
            TighteningMode.EvidenceGated when evidence.HasEvidence && evidence.DataClean => (true, false),
            TighteningMode.EvidenceGated => (false, false),
            TighteningMode.Aggressive => (true, !evidence.HasEvidence || !evidence.DataClean)
                .ApplyRequiresRemediation(evidence.Rationales),
            _ => (false, false)
        };
    }

    private UniqueIndexEvidence EvaluateSingleColumnEvidence(
        ColumnCoordinate coordinate,
        SortedSet<string> rationales)
    {
        UniqueCandidateProfile? profile = null;
        if (_uniqueProfiles.TryGetValue(coordinate, out var foundProfile))
        {
            profile = foundProfile;
        }

        var hasProfile = profile is not null;
        var hasDuplicates = profile?.HasDuplicate ?? false;
        var dataClean = profile is not null && !profile.HasDuplicate;
        var hasEvidence = hasProfile;

        if (!hasProfile)
        {
            if (_evidence.SingleColumnDuplicates.Contains(coordinate))
            {
                hasEvidence = true;
                hasDuplicates = true;
            }
            else if (_evidence.SingleColumnClean.Contains(coordinate))
            {
                hasEvidence = true;
                dataClean = true;
            }
        }

        AppendEvidenceRationales(hasDuplicates, dataClean, rationales, TighteningRationales.UniqueDuplicatesPresent, TighteningRationales.UniqueNoNulls);

        return new UniqueIndexEvidence(hasProfile, hasEvidence, hasDuplicates, dataClean, rationales);
    }

    private UniqueIndexEvidence EvaluateCompositeEvidence(
        EntityModel entity,
        IndexModel index,
        IReadOnlyList<ColumnCoordinate> columnCoordinates,
        SortedSet<string> rationales)
    {
        var key = UniqueIndexEvidenceKey.Create(
            entity.Schema.Value,
            entity.PhysicalName.Value,
            index.Columns.Select(static c => c.Column.Value));

        CompositeUniqueCandidateProfile? profile = null;
        if (_evidence.CompositeProfiles.TryGetValue(key, out var foundProfile))
        {
            profile = foundProfile;
        }

        var hasProfile = profile is not null;
        var hasEvidence = hasProfile;
        var hasDuplicates = profile?.HasDuplicate ?? false;
        var dataClean = profile is not null && !profile.HasDuplicate;

        if (!hasProfile)
        {
            if (columnCoordinates.Any(_evidence.CompositeDuplicates.Contains))
            {
                hasEvidence = true;
                hasDuplicates = true;
            }
            else if (columnCoordinates.All(_evidence.CompositeClean.Contains))
            {
                hasEvidence = true;
                dataClean = true;
            }
        }

        AppendEvidenceRationales(
            hasDuplicates,
            dataClean,
            rationales,
            TighteningRationales.CompositeUniqueDuplicatesPresent,
            TighteningRationales.CompositeUniqueNoNulls);

        return new UniqueIndexEvidence(hasProfile, hasEvidence, hasDuplicates, dataClean, rationales);
    }

    private bool IsPhysicalUnique(ColumnCoordinate coordinate)
        => _columnProfiles.TryGetValue(coordinate, out var profile) && profile.IsUniqueKey;

    private static void AppendEvidenceRationales(
        bool hasDuplicates,
        bool dataClean,
        SortedSet<string> rationales,
        string duplicateRationale,
        string cleanRationale)
    {
        if (hasDuplicates)
        {
            rationales.Add(duplicateRationale);
        }
        else if (dataClean)
        {
            rationales.Add(cleanRationale);
        }
    }

    private bool AddRemediation(SortedSet<string> rationales)
    {
        rationales.Add(TighteningRationales.RemediateBeforeTighten);
        return true;
    }

    private sealed record UniqueIndexEvidence(
        bool HasProfile,
        bool HasEvidence,
        bool HasDuplicates,
        bool DataClean,
        SortedSet<string> Rationales);
}

internal static class UniqueIndexDecisionStrategyExtensions
{
    public static (bool EnforceUnique, bool RequiresRemediation) ApplyRequiresRemediation(
        this (bool EnforceUnique, bool RequiresRemediation) result,
        SortedSet<string> rationales)
    {
        if (!result.EnforceUnique)
        {
            return result;
        }

        if (result.RequiresRemediation)
        {
            rationales.Add(TighteningRationales.RemediateBeforeTighten);
            return result;
        }

        return result;
    }
}
