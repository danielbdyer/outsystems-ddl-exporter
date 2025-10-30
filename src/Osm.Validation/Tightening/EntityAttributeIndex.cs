using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Validation.Tightening;

internal sealed class EntityAttributeIndex
{
    private readonly IReadOnlyDictionary<EntityModel, ImmutableArray<AttributeModel>> _attributesByEntity;
    private readonly IReadOnlyDictionary<ColumnCoordinate, AttributeModel> _attributesByCoordinate;
    private readonly IReadOnlyDictionary<ColumnCoordinate, EntityAttributeIndexEntry> _entriesByCoordinate;
    private readonly ImmutableArray<EntityAttributeIndexEntry> _entries;

    private EntityAttributeIndex(
        IReadOnlyDictionary<EntityModel, ImmutableArray<AttributeModel>> attributesByEntity,
        IReadOnlyDictionary<ColumnCoordinate, AttributeModel> attributesByCoordinate,
        IReadOnlyDictionary<ColumnCoordinate, EntityAttributeIndexEntry> entriesByCoordinate,
        ImmutableArray<EntityAttributeIndexEntry> entries)
    {
        _attributesByEntity = attributesByEntity ?? throw new ArgumentNullException(nameof(attributesByEntity));
        _attributesByCoordinate = attributesByCoordinate ?? throw new ArgumentNullException(nameof(attributesByCoordinate));
        _entriesByCoordinate = entriesByCoordinate ?? throw new ArgumentNullException(nameof(entriesByCoordinate));
        _entries = entries;
    }

    public static EntityAttributeIndex Create(OsmModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var attributesByEntity = new Dictionary<EntityModel, ImmutableArray<AttributeModel>>();
        var attributesByCoordinate = new Dictionary<ColumnCoordinate, AttributeModel>();
        var entries = ImmutableArray.CreateBuilder<EntityAttributeIndexEntry>();
        var entriesByCoordinate = new Dictionary<ColumnCoordinate, EntityAttributeIndexEntry>();

        foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
        {
            attributesByEntity[entity] = entity.Attributes;

            foreach (var attribute in entity.Attributes)
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
                attributesByCoordinate[coordinate] = attribute;
                var entry = new EntityAttributeIndexEntry(entity, attribute, coordinate);
                entries.Add(entry);
                entriesByCoordinate[coordinate] = entry;
            }
        }

        return new EntityAttributeIndex(attributesByEntity, attributesByCoordinate, entriesByCoordinate, entries.ToImmutable());
    }

    public ImmutableArray<AttributeModel> GetAttributes(EntityModel entity)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (_attributesByEntity.TryGetValue(entity, out var attributes))
        {
            return attributes;
        }

        return entity.Attributes;
    }

    public bool TryGetAttribute(ColumnCoordinate coordinate, out AttributeModel attribute)
    {
        if (_attributesByCoordinate.TryGetValue(coordinate, out var value))
        {
            attribute = value;
            return true;
        }

        attribute = default!;
        return false;
    }

    public bool TryGetEntry(ColumnCoordinate coordinate, out EntityAttributeIndexEntry entry)
    {
        if (_entriesByCoordinate.TryGetValue(coordinate, out var value))
        {
            entry = value;
            return true;
        }

        entry = default;
        return false;
    }

    public ImmutableArray<EntityAttributeIndexEntry> Entries => _entries;

    internal readonly record struct EntityAttributeIndexEntry(
        EntityModel Entity,
        AttributeModel Attribute,
        ColumnCoordinate Coordinate);
}
