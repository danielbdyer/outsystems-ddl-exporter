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
                if (lookup.TryGetValue(moduleName.Value, out var module))
                {
                    selected.Add(module);
                }
                else
                {
                    missing.Add(moduleName.Value);
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

        var filteredModules = new List<ModuleModel>();

        foreach (var module in selected)
        {
            if (!options.IncludeSystemModules && module.IsSystemModule)
            {
                continue;
            }

            if (!options.IncludeInactiveModules)
            {
                if (!module.IsActive)
                {
                    continue;
                }

                var activeEntities = module.Entities
                    .Where(static entity => entity.IsActive)
                    .ToImmutableArray();

                if (activeEntities.IsDefaultOrEmpty)
                {
                    continue;
                }

                filteredModules.Add(module with { Entities = activeEntities });
                continue;
            }

            filteredModules.Add(module);
        }

        var materialized = filteredModules.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return ValidationError.Create(
                "modelFilter.modules.empty",
                "Module filter removed all modules from the model.");
        }

        if (!options.EntityFilters.IsEmpty)
        {
            var adjustedModules = ImmutableArray.CreateBuilder<ModuleModel>(materialized.Length);
            foreach (var module in materialized)
            {
                if (!options.EntityFilters.TryGetValue(module.Name.Value, out var entityFilter))
                {
                    adjustedModules.Add(module);
                    continue;
                }

                var filteredEntities = module.Entities
                    .Where(entityFilter.Matches)
                    .ToImmutableArray();

                var missing = entityFilter.GetMissingNames(module.Entities);
                if (!missing.IsDefaultOrEmpty)
                {
                    return ValidationError.Create(
                        "modelFilter.entities.missing",
                        $"Module '{module.Name.Value}' does not contain entity(ies): {string.Join(", ", missing)}.");
                }

                if (filteredEntities.IsDefaultOrEmpty)
                {
                    return ValidationError.Create(
                        "modelFilter.entities.empty",
                        $"Entity filter removed all entities from module '{module.Name.Value}'.");
                }

                adjustedModules.Add(module with { Entities = filteredEntities });
            }

            materialized = adjustedModules.ToImmutable();
        }

        return OsmModel.Create(model.ExportedAtUtc, materialized, model.Sequences, model.ExtendedProperties);
    }
}
