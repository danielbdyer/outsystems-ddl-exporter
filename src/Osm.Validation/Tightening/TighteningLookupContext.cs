using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

internal sealed class TighteningLookupContext
{
    private TighteningLookupContext(
        OsmModel model,
        ProfileSnapshot snapshot,
        TighteningOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        EntityAttributeIndex attributeIndex,
        ForeignKeyTargetIndex foreignKeyTargets,
        UniqueIndexEvidenceAggregator uniqueEvidence,
        EntityLookupResolution lookupResolution)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        ColumnProfiles = columnProfiles ?? throw new ArgumentNullException(nameof(columnProfiles));
        UniqueProfiles = uniqueProfiles ?? throw new ArgumentNullException(nameof(uniqueProfiles));
        ForeignKeyReality = foreignKeyReality ?? throw new ArgumentNullException(nameof(foreignKeyReality));
        AttributeIndex = attributeIndex ?? throw new ArgumentNullException(nameof(attributeIndex));
        ForeignKeyTargets = foreignKeyTargets ?? throw new ArgumentNullException(nameof(foreignKeyTargets));
        UniqueEvidence = uniqueEvidence ?? throw new ArgumentNullException(nameof(uniqueEvidence));
        LookupResolution = lookupResolution ?? throw new ArgumentNullException(nameof(lookupResolution));
    }

    public OsmModel Model { get; }

    public ProfileSnapshot Snapshot { get; }

    public TighteningOptions Options { get; }

    public IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> ColumnProfiles { get; }

    public IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> UniqueProfiles { get; }

    public IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> ForeignKeyReality { get; }

    public EntityAttributeIndex AttributeIndex { get; }

    public ForeignKeyTargetIndex ForeignKeyTargets { get; }

    public UniqueIndexEvidenceAggregator UniqueEvidence { get; }

    public EntityLookupResolution LookupResolution { get; }

    public static TighteningLookupContext Create(
        OsmModel model,
        ProfileSnapshot snapshot,
        TighteningOptions options)
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
        var foreignKeyReality = snapshot.ForeignKeys.ToDictionary(
            fk => ColumnCoordinate.From(fk.Reference),
            static fk => fk);

        var lookupResolution = EntityLookupResolver.Resolve(model, options.Emission.NamingOverrides);
        var attributeIndex = EntityAttributeIndex.Create(model);
        var foreignKeyTargets = ForeignKeyTargetIndex.Create(attributeIndex, lookupResolution.Lookup);

        var uniqueEvidence = UniqueIndexEvidenceAggregator.Create(
            model,
            uniqueProfiles,
            snapshot.CompositeUniqueCandidates,
            options.Uniqueness.EnforceSingleColumnUnique,
            options.Uniqueness.EnforceMultiColumnUnique);

        return new TighteningLookupContext(
            model,
            snapshot,
            options,
            columnProfiles,
            uniqueProfiles,
            foreignKeyReality,
            attributeIndex,
            foreignKeyTargets,
            uniqueEvidence,
            lookupResolution);
    }

    public EntityContext CreateEntityContext(EntityModel entity, AttributeModel attribute, ColumnCoordinate coordinate)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var columnProfile = ColumnProfiles.TryGetValue(coordinate, out var profile) ? profile : null;
        var uniqueProfile = UniqueProfiles.TryGetValue(coordinate, out var uniqueCandidate) ? uniqueCandidate : null;
        var foreignKey = ForeignKeyReality.TryGetValue(coordinate, out var fk) ? fk : null;
        var foreignKeyTarget = ForeignKeyTargets.GetTarget(coordinate);

        return new EntityContext(
            entity,
            attribute,
            coordinate,
            columnProfile,
            uniqueProfile,
            foreignKey,
            foreignKeyTarget,
            UniqueEvidence.SingleColumnClean.Contains(coordinate),
            UniqueEvidence.SingleColumnDuplicates.Contains(coordinate),
            UniqueEvidence.CompositeClean.Contains(coordinate),
            UniqueEvidence.CompositeDuplicates.Contains(coordinate));
    }
}
