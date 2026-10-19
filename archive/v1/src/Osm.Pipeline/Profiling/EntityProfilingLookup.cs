using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Profiling;

internal sealed class EntityProfilingLookup
{
    private readonly IReadOnlyDictionary<EntityName, LookupEntry> _lookup;

    private EntityProfilingLookup(IReadOnlyDictionary<EntityName, LookupEntry> lookup)
    {
        _lookup = lookup;
    }

    public static EntityProfilingLookup Create(OsmModel model, NamingOverrideOptions namingOverrides)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (namingOverrides is null)
        {
            throw new ArgumentNullException(nameof(namingOverrides));
        }

        var resolution = EntityLookupResolver.Resolve(model, namingOverrides);
        var lookup = new Dictionary<EntityName, LookupEntry>(resolution.Lookup.Count);

        foreach (var pair in resolution.Lookup)
        {
            var entity = pair.Value;
            var identifier = entity.Attributes.FirstOrDefault(static attribute => attribute.IsIdentifier);
            lookup[pair.Key] = new LookupEntry(entity, identifier);
        }

        return new EntityProfilingLookup(lookup);
    }

    public bool TryGet(EntityName logicalName, out LookupEntry entry)
    {
        return _lookup.TryGetValue(logicalName, out entry);
    }

    internal readonly struct LookupEntry
    {
        public LookupEntry(EntityModel entity, AttributeModel? preferredIdentifier)
        {
            Entity = entity ?? throw new ArgumentNullException(nameof(entity));
            PreferredIdentifier = preferredIdentifier;
        }

        public EntityModel Entity { get; }

        public AttributeModel? PreferredIdentifier { get; }
    }
}
