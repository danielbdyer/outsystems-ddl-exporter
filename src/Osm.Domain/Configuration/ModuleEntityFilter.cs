using System;
using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Domain.Configuration;

public sealed record ModuleEntityFilter
{
    private ModuleEntityFilter(bool includeAll, ImmutableArray<string> entityNames)
    {
        IncludeAll = includeAll;
        EntityNames = entityNames;
    }

    public bool IncludeAll { get; }

    public ImmutableArray<string> EntityNames { get; }

    public static ModuleEntityFilter IncludeAllEntities { get; }
        = new(true, ImmutableArray<string>.Empty);

    public static ModuleEntityFilter IncludeEntities(ImmutableArray<string> entityNames)
    {
        if (entityNames.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Entity names must not be empty when includeAll is false.", nameof(entityNames));
        }

        return new ModuleEntityFilter(false, entityNames);
    }

    public bool Matches(EntityModel entity)
    {
        if (IncludeAll)
        {
            return true;
        }

        foreach (var name in EntityNames)
        {
            if (string.Equals(name, entity.LogicalName.Value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, entity.PhysicalName.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
