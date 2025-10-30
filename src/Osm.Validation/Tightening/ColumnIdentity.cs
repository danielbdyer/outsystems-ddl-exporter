using System;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

public sealed record ColumnIdentity(
    ColumnCoordinate Coordinate,
    ModuleName Module,
    EntityName EntityLogicalName,
    TableName EntityPhysicalName,
    AttributeName AttributeLogicalName)
{
    public string ModuleName => Module.Value;

    public string EntityName => EntityLogicalName.Value;

    public string TableName => EntityPhysicalName.Value;

    public string AttributeName => AttributeLogicalName.Value;

    public static ColumnIdentity From(EntityModel entity, AttributeModel attribute)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
        return new ColumnIdentity(coordinate, entity.Module, entity.LogicalName, entity.PhysicalName, attribute.LogicalName);
    }

    public static ColumnIdentity From(EntityAttributeIndex.EntityAttributeIndexEntry entry)
        => From(entry.Entity, entry.Attribute);

    public static ColumnIdentity From(ColumnProfile profile, EntityAttributeIndex index)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (index is null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        return ResolveFromIndex(ColumnCoordinate.From(profile), index);
    }

    public static ColumnIdentity From(UniqueCandidateProfile profile, EntityAttributeIndex index)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (index is null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        return ResolveFromIndex(ColumnCoordinate.From(profile), index);
    }

    public static ColumnIdentity From(ForeignKeyReference reference, EntityAttributeIndex index)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (index is null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        return ResolveFromIndex(ColumnCoordinate.From(reference), index);
    }

    public static ColumnIdentity From(ForeignKeyReality reality, EntityAttributeIndex index)
    {
        if (reality is null)
        {
            throw new ArgumentNullException(nameof(reality));
        }

        return From(reality.Reference, index);
    }

    private static ColumnIdentity ResolveFromIndex(ColumnCoordinate coordinate, EntityAttributeIndex index)
    {
        if (!index.TryGetEntry(coordinate, out var entry))
        {
            throw new InvalidOperationException($"Column '{coordinate}' could not be resolved to an entity attribute.");
        }

        return From(entry);
    }
}
