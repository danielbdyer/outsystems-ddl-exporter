using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.Model.Emission;

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
        var snapshot = EntityEmissionSnapshot.Create(moduleName, entity);

        return new EntityEmissionContext(
            snapshot.ModuleName,
            snapshot.Entity,
            snapshot.EmittableAttributes,
            snapshot.IdentifierAttributes,
            snapshot.AttributeLookup,
            snapshot.ActiveIdentifier,
            snapshot.FallbackIdentifier);
    }

    public AttributeModel? GetPreferredIdentifier() => ActiveIdentifier ?? FallbackIdentifier;
}
