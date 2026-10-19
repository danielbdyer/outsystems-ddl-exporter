using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Domain.Model.Emission;

public sealed record EntityEmissionSnapshot(
    string ModuleName,
    EntityModel Entity,
    ImmutableArray<AttributeModel> EmittableAttributes,
    ImmutableArray<AttributeModel> IdentifierAttributes,
    IReadOnlyDictionary<string, AttributeModel> AttributeLookup,
    AttributeModel? ActiveIdentifier,
    AttributeModel? FallbackIdentifier)
{
    public static EntityEmissionSnapshot Create(string moduleName, EntityModel entity)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var emittableBuilder = ImmutableArray.CreateBuilder<AttributeModel>();
        var identifierBuilder = ImmutableArray.CreateBuilder<AttributeModel>();
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

            if (!attribute.IsActive || attribute.Reality.IsPresentButInactive)
            {
                continue;
            }

            emittableBuilder.Add(attribute);
            attributeLookup[attribute.ColumnName.Value] = attribute;

            if (attribute.IsIdentifier)
            {
                identifierBuilder.Add(attribute);

                if (activeIdentifier is null)
                {
                    activeIdentifier = attribute;
                }
            }
        }

        return new EntityEmissionSnapshot(
            moduleName,
            entity,
            emittableBuilder.ToImmutable(),
            identifierBuilder.ToImmutable(),
            attributeLookup,
            activeIdentifier,
            fallbackIdentifier);
    }

    public AttributeModel? PreferredIdentifier => ActiveIdentifier ?? FallbackIdentifier;
}
