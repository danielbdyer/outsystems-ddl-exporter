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
        ImmutableDictionary<string, ModuleEntityFilterOptions> entityFilters)
    {
        Modules = modules;
        IncludeSystemModules = includeSystemModules;
        IncludeInactiveModules = includeInactiveModules;
        EntityFilters = entityFilters;
    }

    public ImmutableArray<ModuleName> Modules { get; }

    public bool IncludeSystemModules { get; }

    public bool IncludeInactiveModules { get; }

    public ImmutableDictionary<string, ModuleEntityFilterOptions> EntityFilters { get; }

    public static ModuleFilterOptions IncludeAll { get; } = new(
        ImmutableArray<ModuleName>.Empty,
        includeSystemModules: true,
        includeInactiveModules: true,
        ImmutableDictionary<string, ModuleEntityFilterOptions>.Empty);

    public bool HasFilter
        => !Modules.IsDefaultOrEmpty
            || !IncludeSystemModules
            || !IncludeInactiveModules
            || !EntityFilters.IsEmpty;

    public static Result<ModuleFilterOptions> Create(
        IEnumerable<string>? modules,
        bool includeSystemModules,
        bool includeInactiveModules,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? entityFilters = null)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleName>();
        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        if (modules is not null)
        {
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
        }

        var normalized = builder.ToImmutable();
        if (!normalized.IsDefaultOrEmpty)
        {
            normalized = normalized.Sort(Comparer<ModuleName>.Create(static (left, right)
                => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)));
        }

        var entityFilterOptions = ImmutableDictionary<string, ModuleEntityFilterOptions>.Empty;
        if (entityFilters is { Count: > 0 })
        {
            var filterBuilder = ImmutableDictionary.CreateBuilder<string, ModuleEntityFilterOptions>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in entityFilters)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    errors.Add(ValidationError.Create(
                        "moduleFilter.entities.module.empty",
                        "Module name for entity filter must not be null or whitespace."));
                    continue;
                }

                var moduleKey = kvp.Key.Trim();
                if (kvp.Value is null)
                {
                    continue;
                }

                var filterResult = ModuleEntityFilterOptions.Create(kvp.Value);
                if (filterResult.IsFailure)
                {
                    foreach (var error in filterResult.Errors)
                    {
                        errors.Add(ValidationError.Create(
                            error.Code,
                            $"Module '{moduleKey}' entity filter invalid: {error.Message}"));
                    }

                    continue;
                }

                filterBuilder[moduleKey] = filterResult.Value;
            }

            entityFilterOptions = filterBuilder.ToImmutable();
        }

        if (errors.Count > 0)
        {
            return Result<ModuleFilterOptions>.Failure(errors.ToImmutable());
        }

        return new ModuleFilterOptions(normalized, includeSystemModules, includeInactiveModules, entityFilterOptions);
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
}
