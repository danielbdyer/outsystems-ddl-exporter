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

        if (!options.EntityFilters.IsEmpty)
        {
            var updatedModules = ImmutableArray.CreateBuilder<ModuleModel>(materialized.Length);
            foreach (var module in materialized)
            {
                if (!options.EntityFilters.TryGetValue(module.Name, out var entityFilter) || entityFilter.IncludeAll)
                {
                    updatedModules.Add(module);
                    continue;
                }

                var missingEntities = entityFilter.EntityNames
                    .Where(entityName => !module.Entities.Any(entity
                        => string.Equals(entity.LogicalName.Value, entityName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(entity.PhysicalName.Value, entityName, StringComparison.OrdinalIgnoreCase)))
                    .ToImmutableArray();

                if (!missingEntities.IsDefaultOrEmpty)
                {
                    return ValidationError.Create(
                        "modelFilter.entities.missing",
                        $"Module '{module.Name.Value}' does not contain the requested entity/entities: {string.Join(", ", missingEntities)}.");
                }

                var filteredEntities = module.Entities
                    .Where(entityFilter.Matches)
                    .ToImmutableArray();

                if (filteredEntities.IsDefaultOrEmpty)
                {
                    return ValidationError.Create(
                        "modelFilter.entities.empty",
                        $"Entity filter removed all entities from module '{module.Name.Value}'.");
                }

                updatedModules.Add(module with { Entities = filteredEntities });
            }

            materialized = updatedModules.ToImmutable();
        }

        return OsmModel.Create(model.ExportedAtUtc, materialized, model.Sequences, model.ExtendedProperties);
    }
}
