using System;
using System.Collections.Generic;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

internal sealed class ForeignKeyTargetIndex
{
    private readonly IReadOnlyDictionary<ColumnCoordinate, EntityModel?> _targets;

    private ForeignKeyTargetIndex(IReadOnlyDictionary<ColumnCoordinate, EntityModel?> targets)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    public static ForeignKeyTargetIndex Create(
        EntityAttributeIndex attributeIndex,
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup)
    {
        if (attributeIndex is null)
        {
            throw new ArgumentNullException(nameof(attributeIndex));
        }

        if (entityLookup is null)
        {
            throw new ArgumentNullException(nameof(entityLookup));
        }

        var targets = new Dictionary<ColumnCoordinate, EntityModel?>();

        foreach (var entry in attributeIndex.Entries)
        {
            if (!entry.Attribute.Reference.IsReference)
            {
                continue;
            }

            EntityModel? target = null;
            if (entry.Attribute.Reference.TargetEntity is EntityName targetName &&
                entityLookup.TryGetValue(targetName, out var resolved))
            {
                target = resolved;
            }

            targets[entry.Coordinate] = target;
        }

        return new ForeignKeyTargetIndex(targets);
    }

    public EntityModel? GetTarget(ColumnCoordinate coordinate)
        => _targets.TryGetValue(coordinate, out var target) ? target : null;
}
