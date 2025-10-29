using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Smo;

internal sealed record EntityEmissionContext(
    string ModuleName,
    EntityModel Entity,
    ImmutableArray<AttributeModel> EmittableAttributes,
    ImmutableArray<AttributeModel> IdentifierAttributes,
    IReadOnlyDictionary<string, AttributeModel> AttributeLookup,
    AttributeModel? ActiveIdentifier,
    AttributeModel? FallbackIdentifier)
{
    public static EntityEmissionContext Create(string moduleName, EntityModel entity)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var emittableBuilder = ImmutableArray.CreateBuilder<AttributeModel>();
        var attributeLookup = new Dictionary<string, AttributeModel>(StringComparer.OrdinalIgnoreCase);
        AttributeModel? activeIdentifier = null;
        AttributeModel? fallbackIdentifier = null;

        foreach (var attribute in entity.Attributes)
        {
            if (attribute is null)
            {
                continue;
            }

            if (attribute.IsIdentifier && fallbackIdentifier is null)
            {
                fallbackIdentifier = attribute;
            }

            if (!IsEmittableAttribute(attribute))
            {
                continue;
            }

            emittableBuilder.Add(attribute);
            attributeLookup[attribute.ColumnName.Value] = attribute;

            if (attribute.IsIdentifier && activeIdentifier is null)
            {
                activeIdentifier = attribute;
            }
        }

        var orderedAttributes = emittableBuilder.ToImmutable();
        var identifierBuilder = ImmutableArray.CreateBuilder<AttributeModel>();

        foreach (var attribute in orderedAttributes)
        {
            if (attribute.IsIdentifier)
            {
                identifierBuilder.Add(attribute);
            }
        }

        return new EntityEmissionContext(
            moduleName,
            entity,
            orderedAttributes,
            identifierBuilder.ToImmutable(),
            attributeLookup,
            activeIdentifier,
            fallbackIdentifier);
    }

    public AttributeModel? GetPreferredIdentifier() => ActiveIdentifier ?? FallbackIdentifier;

    private static bool IsEmittableAttribute(AttributeModel attribute)
    {
        if (attribute is null)
        {
            return false;
        }

        if (!attribute.IsActive)
        {
            return false;
        }

        return !attribute.Reality.IsPresentButInactive;
    }
}
