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
    private readonly IReadOnlyDictionary<EntityName, Entry> _lookup;

    private EntityProfilingLookup(IReadOnlyDictionary<EntityName, Entry> lookup)
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
        var entries = new Dictionary<EntityName, Entry>();

        foreach (var kvp in resolution.Lookup)
        {
            var identifier = kvp.Value.Attributes.FirstOrDefault(static attribute => attribute.IsIdentifier);
            entries[kvp.Key] = new Entry(kvp.Value, identifier);
        }

        return new EntityProfilingLookup(entries);
    }

    public bool TryGet(EntityName logicalName, out Entry entry)
    {
        return _lookup.TryGetValue(logicalName, out entry);
    }

    internal sealed record Entry(EntityModel Entity, AttributeModel? PreferredIdentifier);
}
