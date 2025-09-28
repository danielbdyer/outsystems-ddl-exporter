using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Pipeline.ModelIngestion;

public sealed class ModuleFilter
{
    public Result<OsmModel> Apply(OsmModel model, ModuleFilterOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!options.HasFilter)
        {
            return model;
        }

        var modules = model.Modules;
        var selected = new List<ModuleModel>();
        var lookup = modules.ToDictionary(static m => m.Name.Value, StringComparer.OrdinalIgnoreCase);

        if (!options.Modules.IsDefaultOrEmpty)
        {
            var missing = new List<string>();
            foreach (var moduleName in options.Modules)
            {
                if (lookup.TryGetValue(moduleName, out var module))
                {
                    selected.Add(module);
                }
                else
                {
                    missing.Add(moduleName);
                }
            }

            if (missing.Count > 0)
            {
                return ValidationError.Create(
                    "modelFilter.modules.missing",
                    $"Requested module(s) not found in model: {string.Join(", ", missing)}.");
            }
        }
        else
        {
            selected.AddRange(modules);
        }

        IEnumerable<ModuleModel> filtered = selected;

        if (!options.IncludeSystemModules)
        {
            filtered = filtered.Where(static module => !module.IsSystemModule);
        }

        if (!options.IncludeInactiveModules)
        {
            filtered = filtered.Where(static module => module.IsActive);
        }

        var materialized = filtered.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return ValidationError.Create(
                "modelFilter.modules.empty",
                "Module filter removed all modules from the model.");
        }

        return OsmModel.Create(model.ExportedAtUtc, materialized);
    }
}
