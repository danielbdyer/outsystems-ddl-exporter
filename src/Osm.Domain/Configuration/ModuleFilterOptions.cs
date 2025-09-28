using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Configuration;

public sealed record ModuleFilterOptions
{
    private ModuleFilterOptions(ImmutableArray<string> modules, bool includeSystemModules, bool includeInactiveModules)
    {
        Modules = modules;
        IncludeSystemModules = includeSystemModules;
        IncludeInactiveModules = includeInactiveModules;
    }

    public ImmutableArray<string> Modules { get; }

    public bool IncludeSystemModules { get; }

    public bool IncludeInactiveModules { get; }

    public static ModuleFilterOptions IncludeAll { get; } = new(
        ImmutableArray<string>.Empty,
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
                ImmutableArray<string>.Empty,
                includeSystemModules,
                includeInactiveModules);
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in modules)
        {
            if (candidate is null)
            {
                return ValidationError.Create(
                    "moduleFilter.modules.null",
                    "Module names must not be null.");
            }

            var trimmed = candidate.Trim();
            if (trimmed.Length == 0)
            {
                return ValidationError.Create(
                    "moduleFilter.modules.empty",
                    "Module names must not be empty or whitespace.");
            }

            if (seen.Add(trimmed))
            {
                builder.Add(trimmed);
            }
        }

        var normalized = builder.ToImmutable();
        if (!normalized.IsDefaultOrEmpty)
        {
            normalized = normalized.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return new ModuleFilterOptions(normalized, includeSystemModules, includeInactiveModules);
    }

    public ModuleFilterOptions Merge(IEnumerable<string> modules)
    {
        if (modules is null)
        {
            throw new ArgumentNullException(nameof(modules));
        }

        if (!modules.Any())
        {
            return this;
        }

        var combined = new HashSet<string>(Modules, StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (module is null)
            {
                continue;
            }

            var trimmed = module.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            combined.Add(trimmed);
        }

        if (combined.Count == Modules.Length)
        {
            return this;
        }

        var normalized = combined
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        return new ModuleFilterOptions(normalized, IncludeSystemModules, IncludeInactiveModules);
    }

    public ModuleFilterOptions WithIncludeSystemModules(bool include)
        => new(Modules, include, IncludeInactiveModules);

    public ModuleFilterOptions WithIncludeInactiveModules(bool include)
        => new(Modules, IncludeSystemModules, include);
}
