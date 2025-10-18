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

    public UniqueIndexAnalysis Evaluate(EntityModel entity, IndexModel index)
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
            var emptyDecision = UniqueIndexDecision.Create(coordinate, enforceUnique: false, requiresRemediation: false, ImmutableArray<string>.Empty);
            return new UniqueIndexAnalysis(
                coordinate,
                emptyDecision,
                ImmutableArray<string>.Empty,
                false,
                false,
                false,
                false,
                false,
                ImmutableArray<ColumnCoordinate>.Empty);
        }

        var keyColumns = index.Columns
            .Where(static c => !c.IsIncluded)
            .ToArray();

        if (keyColumns.Length == 0)
        {
            var emptyDecision = UniqueIndexDecision.Create(coordinate, false, false, ImmutableArray<string>.Empty);
            return new UniqueIndexAnalysis(
                coordinate,
                emptyDecision,
                ImmutableArray<string>.Empty,
                false,
                false,
                false,
                false,
                false,
                ImmutableArray<ColumnCoordinate>.Empty);
        }

        var columnCoordinates = keyColumns
            .Select(c => new ColumnCoordinate(entity.Schema, entity.PhysicalName, c.Column))
            .ToArray();

        var physicalUnique = columnCoordinates.All(IsPhysicalUnique);
        var isComposite = columnCoordinates.Length > 1;
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
            ? EvaluateCompositeEvidence(entity, keyColumns, columnCoordinates, rationales)
            : EvaluateSingleColumnEvidence(columnCoordinates[0], rationales);

        if (!evidence.HasProfile && !physicalUnique)
        {
            rationales.Add(TighteningRationales.ProfileMissing);
        }

        var (enforceUnique, requiresRemediation) = DetermineOutcome(policyDisabled, physicalUnique, evidence);
        var uniqueDecision = UniqueIndexDecision.Create(coordinate, enforceUnique, requiresRemediation, rationales.ToImmutableArray());

        return new UniqueIndexAnalysis(
            coordinate,
            uniqueDecision,
            rationales.ToImmutableArray(),
            evidence.HasDuplicates,
            physicalUnique,
            policyDisabled,
            evidence.HasEvidence,
            evidence.DataClean,
            columnCoordinates.ToImmutableArray());
    }

    public UniqueIndexDecision Decide(EntityModel entity, IndexModel index)
        => Evaluate(entity, index).Decision;

    private (bool EnforceUnique, bool RequiresRemediation) DetermineOutcome(
        bool policyDisabled,
        bool physicalUnique,
        UniqueIndexEvidence evidence)
    {
        var matrix = TighteningPolicyMatrix.UniqueIndexes;
        var mode = _options.Policy.Mode;

        if (policyDisabled)
        {
            return ApplyOutcome(matrix.Resolve(mode, TighteningPolicyMatrix.UniquePolicyScenario.PolicyDisabled), evidence);
        }

        if (evidence.HasDuplicates)
        {
            var scenario = physicalUnique
                ? TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithPhysicalReality
                : TighteningPolicyMatrix.UniquePolicyScenario.DuplicatesWithoutPhysicalReality;

            return ApplyOutcome(matrix.Resolve(mode, scenario), evidence);
        }

        if (physicalUnique)
        {
            return ApplyOutcome(matrix.Resolve(mode, TighteningPolicyMatrix.UniquePolicyScenario.PhysicalReality), evidence);
        }

        if (evidence.HasEvidence && evidence.DataClean)
        {
            return ApplyOutcome(matrix.Resolve(mode, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithEvidence), evidence);
        }

        return ApplyOutcome(matrix.Resolve(mode, TighteningPolicyMatrix.UniquePolicyScenario.CleanWithoutEvidence), evidence);
    }

    private (bool EnforceUnique, bool RequiresRemediation) ApplyOutcome(
        TighteningPolicyMatrix.UniqueIndexOutcome outcome,
        UniqueIndexEvidence evidence)
    {
        if (!outcome.EnforceUnique)
        {
            return (false, false);
        }

        return outcome.Remediation switch
        {
            TighteningPolicyMatrix.RemediationDirective.None => (true, false),
            TighteningPolicyMatrix.RemediationDirective.Always => (true, AddRemediation(evidence.Rationales)),
            TighteningPolicyMatrix.RemediationDirective.WhenEvidenceMissing => (true, RequireEvidenceOrRemediate(evidence)),
            _ => (true, false)
        };
    }

    private bool RequireEvidenceOrRemediate(UniqueIndexEvidence evidence)
    {
        if (evidence.HasEvidence && evidence.DataClean)
        {
            return false;
        }

        return AddRemediation(evidence.Rationales);
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
        IReadOnlyList<IndexColumnModel> keyColumns,
        IReadOnlyList<ColumnCoordinate> columnCoordinates,
        SortedSet<string> rationales)
    {
        var key = UniqueIndexEvidenceKey.Create(
            entity.Schema.Value,
            entity.PhysicalName.Value,
            keyColumns.Select(static c => c.Column.Value));

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

    public sealed record UniqueIndexAnalysis(
        IndexCoordinate Index,
        UniqueIndexDecision Decision,
        ImmutableArray<string> Rationales,
        bool HasDuplicates,
        bool PhysicalReality,
        bool PolicyDisabled,
        bool HasEvidence,
        bool DataClean,
        ImmutableArray<ColumnCoordinate> Columns);

    private sealed record UniqueIndexEvidence(
        bool HasProfile,
        bool HasEvidence,
        bool HasDuplicates,
        bool DataClean,
        SortedSet<string> Rationales);
}
