using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;

namespace Osm.Pipeline.Orchestration;

public sealed record ModuleFilterSnapshot(
    bool HasFilter,
    IReadOnlyList<string> Modules,
    bool IncludeSystemModules,
    bool IncludeInactiveModules,
    int EntityFilterModuleCount,
    bool HasValidationOverrides)
{
    public static ModuleFilterSnapshot Create(ModuleFilterOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var modules = options.Modules.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : options.Modules.Select(module => module.Value).ToArray();

        var entityFilterCount = options.EntityFilters.Count;
        var hasValidationOverrides = !options.ValidationOverrides.IsEmpty;

        return new ModuleFilterSnapshot(
            options.HasFilter,
            modules,
            options.IncludeSystemModules,
            options.IncludeInactiveModules,
            entityFilterCount,
            hasValidationOverrides);
    }

    public void Apply(PipelineLogMetadataBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder
            .WithFlag("moduleFilter.hasFilter", HasFilter)
            .WithCount("moduleFilter.modules", Modules.Count)
            .WithFlag("moduleFilter.includeSystemModules", IncludeSystemModules)
            .WithFlag("moduleFilter.includeInactiveModules", IncludeInactiveModules)
            .WithCount("moduleFilter.entityFilters", EntityFilterModuleCount)
            .WithFlag("moduleFilter.hasValidationOverrides", HasValidationOverrides);
    }
}
