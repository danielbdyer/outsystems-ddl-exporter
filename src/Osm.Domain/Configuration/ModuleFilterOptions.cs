using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Configuration;

public sealed record ModuleFilterOptions
{
    private ModuleFilterOptions(
        ImmutableArray<ModuleName> modules,
        bool includeSystemModules,
        bool includeInactiveModules,
        ImmutableDictionary<ModuleName, ModuleEntityFilter> entityFilters)
    {
        Modules = modules;
        IncludeSystemModules = includeSystemModules;
        IncludeInactiveModules = includeInactiveModules;
        EntityFilters = entityFilters;
    }

    public ImmutableArray<ModuleName> Modules { get; }

    public bool IncludeSystemModules { get; }

    public bool IncludeInactiveModules { get; }

    public ImmutableDictionary<ModuleName, ModuleEntityFilter> EntityFilters { get; }

    public static ModuleFilterOptions IncludeAll { get; } = new(
        ImmutableArray<ModuleName>.Empty,
        includeSystemModules: true,
        includeInactiveModules: true,
        ImmutableDictionary<ModuleName, ModuleEntityFilter>.Empty);

    public bool HasFilter
        => !Modules.IsDefaultOrEmpty
            || !IncludeSystemModules
            || !IncludeInactiveModules
            || EntityFilters.Count > 0;

    public static Result<ModuleFilterOptions> Create(
        IEnumerable<string>? modules,
        bool includeSystemModules,
        bool includeInactiveModules,
        IEnumerable<ModuleEntityFilterDefinition>? entityFilters = null)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        var normalizedModules = modules is null
            ? ImmutableArray<ModuleName>.Empty
            : NormalizeModules(modules, errors);

        var normalizedFilters = NormalizeEntityFilters(entityFilters ?? Array.Empty<ModuleEntityFilterDefinition>(), errors);

        if (errors.Count > 0)
        {
            return Result<ModuleFilterOptions>.Failure(errors.ToImmutable());
        }

        return new ModuleFilterOptions(normalizedModules, includeSystemModules, includeInactiveModules, normalizedFilters);
    }

    private static ImmutableArray<ModuleName> NormalizeModules(
        IEnumerable<string> modules,
        ImmutableArray<ValidationError>.Builder errors)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleName>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var candidate in modules)
        {
            if (candidate is null)
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.modules.null",
                    $"Module name at position {index} must not be null."));
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.modules.empty",
                    $"Module name at position {index} must not be empty or whitespace."));
                index++;
                continue;
            }

            var trimmed = candidate.Trim();
            var moduleResult = ModuleName.Create(trimmed);
            if (moduleResult.IsFailure)
            {
                foreach (var error in moduleResult.Errors)
                {
                    errors.Add(ValidationError.Create(
                        error.Code,
                        $"Module name '{trimmed}' is invalid: {error.Message}"));
                }

                index++;
                continue;
            }

            var moduleName = moduleResult.Value;
            if (seen.Add(moduleName.Value))
            {
                builder.Add(moduleName);
            }

            index++;
        }

        if (builder.Count == 0)
        {
            return ImmutableArray<ModuleName>.Empty;
        }

        var normalized = builder.ToImmutable();
        return normalized.Sort(Comparer<ModuleName>.Create(static (left, right)
            => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)));
    }

    public ModuleFilterOptions Merge(IEnumerable<ModuleName> modules)
    {
        if (modules is null)
        {
            throw new ArgumentNullException(nameof(modules));
        }

        var materialized = modules.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return this;
        }

        var combined = new Dictionary<string, ModuleName>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in Modules)
        {
            combined[module.Value] = module;
        }

        foreach (var module in materialized)
        {
            if (!combined.ContainsKey(module.Value))
            {
                combined[module.Value] = module;
            }
        }

        if (combined.Count == Modules.Length)
        {
            return this;
        }

        var normalized = combined
            .Values
            .OrderBy(static value => value.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        return new ModuleFilterOptions(normalized, IncludeSystemModules, IncludeInactiveModules, EntityFilters);
    }

    public ModuleFilterOptions WithIncludeSystemModules(bool include)
        => new(Modules, include, IncludeInactiveModules, EntityFilters);

    public ModuleFilterOptions WithIncludeInactiveModules(bool include)
        => new(Modules, IncludeSystemModules, include, EntityFilters);

    private static ImmutableDictionary<ModuleName, ModuleEntityFilter> NormalizeEntityFilters(
        IEnumerable<ModuleEntityFilterDefinition> definitions,
        ImmutableArray<ValidationError>.Builder errors)
    {
        var filters = ImmutableDictionary.CreateBuilder<ModuleName, ModuleEntityFilter>();

        foreach (var definition in definitions)
        {
            if (definition is null)
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.entities.null",
                    "Module entity filter definition must not be null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(definition.Module))
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.entities.moduleEmpty",
                    "Module entity filter definition must specify a module name."));
                continue;
            }

            var moduleResult = ModuleName.Create(definition.Module.Trim());
            if (moduleResult.IsFailure)
            {
                foreach (var error in moduleResult.Errors)
                {
                    errors.Add(ValidationError.Create(
                        error.Code,
                        $"Module name '{definition.Module.Trim()}' is invalid: {error.Message}"));
                }

                continue;
            }

            var moduleName = moduleResult.Value;

            if (definition.IncludeAllEntities)
            {
                filters[moduleName] = ModuleEntityFilter.IncludeAllEntities;
                continue;
            }

            if (definition.Entities is null || definition.Entities.Count == 0)
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.entities.empty",
                    $"Module '{moduleName.Value}' entity filter must specify at least one entity when includeAll is false."));
                continue;
            }

            var entities = ImmutableArray.CreateBuilder<string>();
            var seenEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var candidate in definition.Entities)
            {
                if (candidate is null)
                {
                    errors.Add(ValidationError.Create(
                        "moduleFilter.entities.null",
                        $"Entity name at position {index} for module '{moduleName.Value}' must not be null."));
                    index++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    errors.Add(ValidationError.Create(
                        "moduleFilter.entities.emptyName",
                        $"Entity name at position {index} for module '{moduleName.Value}' must not be empty or whitespace."));
                    index++;
                    continue;
                }

                var trimmed = candidate.Trim();
                if (seenEntities.Add(trimmed))
                {
                    entities.Add(trimmed);
                }

                index++;
            }

            if (entities.Count == 0)
            {
                continue;
            }

            filters[moduleName] = ModuleEntityFilter.IncludeEntities(entities.ToImmutable());
        }

        return filters.ToImmutable();
    }
}
