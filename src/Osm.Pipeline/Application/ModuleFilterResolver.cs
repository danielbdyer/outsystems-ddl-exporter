using System;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;

namespace Osm.Pipeline.Application;

internal static class ModuleFilterResolver
{
    public static Result<ModuleFilterOptions> Resolve(CliConfiguration configuration, ModuleFilterOverrides overrides)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        overrides ??= new ModuleFilterOverrides(Array.Empty<string>(), null, null);

        var includeSystemModules = overrides.IncludeSystemModules
            ?? configuration.ModuleFilter.IncludeSystemModules
            ?? true;
        var includeInactiveModules = overrides.IncludeInactiveModules
            ?? configuration.ModuleFilter.IncludeInactiveModules
            ?? true;

        var modules = overrides.Modules is { Count: > 0 }
            ? overrides.Modules
            : configuration.ModuleFilter.Modules ?? Array.Empty<string>();

        return ModuleFilterOptions.Create(modules.ToArray(), includeSystemModules, includeInactiveModules);
    }
}
