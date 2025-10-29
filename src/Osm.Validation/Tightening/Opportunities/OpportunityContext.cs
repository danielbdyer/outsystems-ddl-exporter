using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Opportunities;

internal sealed class OpportunityContext
{
    private OpportunityContext(
        EntityAttributeIndex attributeIndex,
        IReadOnlyDictionary<ColumnCoordinate, EntityAttributeIndex.EntityAttributeIndexEntry> attributeLookup,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> compositeProfiles,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeys,
        IReadOnlyDictionary<string, EntityModel> entityLookup,
        IReadOnlyDictionary<IndexCoordinate, (EntityModel Entity, IndexModel Index)> uniqueIndexLookup)
    {
        AttributeIndex = attributeIndex;
        AttributeLookup = attributeLookup;
        ColumnProfiles = columnProfiles;
        UniqueProfiles = uniqueProfiles;
        CompositeProfiles = compositeProfiles;
        ForeignKeys = foreignKeys;
        EntityLookup = entityLookup;
        UniqueIndexLookup = uniqueIndexLookup;
    }

    public EntityAttributeIndex AttributeIndex { get; }

    public IReadOnlyDictionary<ColumnCoordinate, EntityAttributeIndex.EntityAttributeIndexEntry> AttributeLookup { get; }

    public IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> ColumnProfiles { get; }

    public IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> UniqueProfiles { get; }

    public IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> CompositeProfiles { get; }

    public IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> ForeignKeys { get; }

    public IReadOnlyDictionary<string, EntityModel> EntityLookup { get; }

    public IReadOnlyDictionary<IndexCoordinate, (EntityModel Entity, IndexModel Index)> UniqueIndexLookup { get; }

    public static OpportunityContext Create(OsmModel model, ProfileSnapshot profile)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var attributeIndex = EntityAttributeIndex.Create(model);
        var attributeLookup = attributeIndex.Entries.ToDictionary(static entry => entry.Coordinate, static entry => entry);

        var columnProfiles = profile.Columns.IsDefaultOrEmpty
            ? new Dictionary<ColumnCoordinate, ColumnProfile>()
            : profile.Columns.ToDictionary(ColumnCoordinate.From, static column => column);

        var uniqueProfiles = profile.UniqueCandidates.IsDefaultOrEmpty
            ? new Dictionary<ColumnCoordinate, UniqueCandidateProfile>()
            : profile.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static candidate => candidate);

        var compositeProfiles = BuildCompositeProfileLookup(profile.CompositeUniqueCandidates);

        var foreignKeys = profile.ForeignKeys.IsDefaultOrEmpty
            ? new Dictionary<ColumnCoordinate, ForeignKeyReality>()
            : profile.ForeignKeys.ToDictionary(static fk => ColumnCoordinate.From(fk.Reference), static fk => fk);

        var entityLookup = BuildEntityLookup(model);
        var indexLookup = BuildIndexLookup(model);

        return new OpportunityContext(
            attributeIndex,
            attributeLookup,
            columnProfiles,
            uniqueProfiles,
            compositeProfiles,
            foreignKeys,
            entityLookup,
            indexLookup);
    }

    private static IReadOnlyDictionary<string, EntityModel> BuildEntityLookup(OsmModel model)
    {
        var lookup = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
        {
            lookup.TryAdd(entity.LogicalName.Value, entity);
        }

        return lookup;
    }

    private static IReadOnlyDictionary<IndexCoordinate, (EntityModel Entity, IndexModel Index)> BuildIndexLookup(OsmModel model)
    {
        var result = new Dictionary<IndexCoordinate, (EntityModel, IndexModel)>();

        foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
        {
            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var coordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
                result[coordinate] = (entity, index);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> BuildCompositeProfileLookup(
        ImmutableArray<CompositeUniqueCandidateProfile> profiles)
    {
        var result = new Dictionary<string, CompositeUniqueCandidateProfile>(StringComparer.OrdinalIgnoreCase);

        if (profiles.IsDefaultOrEmpty)
        {
            return result;
        }

        foreach (var profile in profiles)
        {
            var key = UniqueIndexEvidenceKey.Create(
                profile.Schema.Value,
                profile.Table.Value,
                profile.Columns.Select(static c => c.Value));

            result[key] = profile;
        }

        return result;
    }
}
