using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Configuration;

public sealed record ModuleFilterOptions
{
    private ModuleFilterOptions(ImmutableArray<ModuleName> modules, bool includeSystemModules, bool includeInactiveModules)
    {
        Modules = modules;
        IncludeSystemModules = includeSystemModules;
        IncludeInactiveModules = includeInactiveModules;
    }

    public ImmutableArray<ModuleName> Modules { get; }

    public bool IncludeSystemModules { get; }

    public bool IncludeInactiveModules { get; }

    public static ModuleFilterOptions IncludeAll { get; } = new(
        ImmutableArray<ModuleName>.Empty,
        includeSystemModules: true,
        includeInactiveModules: true);

    public bool HasFilter => !Modules.IsDefaultOrEmpty || !IncludeSystemModules || !IncludeInactiveModules;

    public static Result<ModuleFilterOptions> Create(
        IEnumerable<string>? modules,
        bool includeSystemModules,
        bool includeInactiveModules)
    {
        if (modules is null)
        {
            return new ModuleFilterOptions(
                ImmutableArray<ModuleName>.Empty,
                includeSystemModules,
                includeInactiveModules);
        }

        var builder = ImmutableArray.CreateBuilder<ModuleName>();
        var errors = ImmutableArray.CreateBuilder<ValidationError>();
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

        if (errors.Count > 0)
        {
            return Result<ModuleFilterOptions>.Failure(errors.ToImmutable());
        }

        var normalized = builder.ToImmutable();
        if (!normalized.IsDefaultOrEmpty)
        {
            normalized = normalized.Sort(Comparer<ModuleName>.Create(static (left, right)
                => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)));
        }

        return new ModuleFilterOptions(normalized, includeSystemModules, includeInactiveModules);
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

        return new ModuleFilterOptions(normalized, IncludeSystemModules, IncludeInactiveModules);
    }

    public ModuleFilterOptions WithIncludeSystemModules(bool include)
        => new(Modules, include, IncludeInactiveModules);

    public ModuleFilterOptions WithIncludeInactiveModules(bool include)
        => new(Modules, IncludeSystemModules, include);
}
