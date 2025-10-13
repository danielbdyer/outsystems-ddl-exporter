using System;
using System.Collections.Generic;

namespace Osm.Domain.Configuration;

public sealed record ModuleEntityFilterDefinition(
    string Module,
    bool IncludeAllEntities,
    IReadOnlyCollection<string> Entities)
{
    public static ModuleEntityFilterDefinition IncludeAll(string module)
    {
        if (module is null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        return new ModuleEntityFilterDefinition(module, IncludeAllEntities: true, Array.Empty<string>());
    }
}
