using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        overrides ??= new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>());

        var includeSystemModules = overrides.IncludeSystemModules
            ?? configuration.ModuleFilter.IncludeSystemModules
            ?? true;
        var includeInactiveModules = overrides.IncludeInactiveModules
            ?? configuration.ModuleFilter.IncludeInactiveModules
            ?? true;

        var modules = overrides.Modules is { Count: > 0 }
            ? overrides.Modules
            : configuration.ModuleFilter.Modules ?? Array.Empty<string>();

        var entityFilters = configuration.ModuleFilter.EntityFilters;

        var configOverrides = configuration.ModuleFilter.ValidationOverrides
            ?? new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase);

        var cliOverridesResult = ParseCliOverrides(overrides);
        if (cliOverridesResult.IsFailure)
        {
            return Result<ModuleFilterOptions>.Failure(cliOverridesResult.Errors);
        }

        var mergedOverrides = MergeOverrides(configOverrides, cliOverridesResult.Value);

        return ModuleFilterOptions.Create(
            modules.ToArray(),
            includeSystemModules,
            includeInactiveModules,
            entityFilters,
            mergedOverrides);
    }

    private static Result<IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration>> ParseCliOverrides(ModuleFilterOverrides overrides)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        var builder = new Dictionary<string, MutableOverride>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in overrides.AllowMissingPrimaryKey ?? Array.Empty<string>())
        {
            if (!TryParseOverrideToken(token, out var module, out var entity, out var error))
            {
                if (error is not null)
                {
                    errors.Add(error.Value);
                }

                continue;
            }

            var entry = GetOrCreate(module);
            if (entity is null)
            {
                entry.AllowMissingPrimaryKeyForAll = true;
                entry.AllowMissingPrimaryKey.Clear();
            }
            else if (!entry.AllowMissingPrimaryKeyForAll)
            {
                entry.AllowMissingPrimaryKey.Add(entity);
            }
        }

        foreach (var token in overrides.AllowMissingSchema ?? Array.Empty<string>())
        {
            if (!TryParseOverrideToken(token, out var module, out var entity, out var error))
            {
                if (error is not null)
                {
                    errors.Add(error.Value);
                }

                continue;
            }

            var entry = GetOrCreate(module);
            if (entity is null)
            {
                entry.AllowMissingSchemaForAll = true;
                entry.AllowMissingSchema.Clear();
            }
            else if (!entry.AllowMissingSchemaForAll)
            {
                entry.AllowMissingSchema.Add(entity);
            }
        }

        if (errors.Count > 0)
        {
            return Result<IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration>>.Failure(errors.ToImmutable());
        }

        var result = new Dictionary<string, ModuleValidationOverrideConfiguration>(builder.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in builder)
        {
            var value = pair.Value;
            result[pair.Key] = new ModuleValidationOverrideConfiguration(
                value.AllowMissingPrimaryKey.ToArray(),
                value.AllowMissingPrimaryKeyForAll,
                value.AllowMissingSchema.ToArray(),
                value.AllowMissingSchemaForAll);
        }

        return result;

        MutableOverride GetOrCreate(string module)
        {
            if (!builder.TryGetValue(module, out var entry))
            {
                entry = new MutableOverride();
                builder[module] = entry;
            }

            return entry;
        }
    }

    private static IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration> MergeOverrides(
        IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration> configurationOverrides,
        IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration> cliOverrides)
    {
        var merged = new Dictionary<string, ModuleValidationOverrideConfiguration>(configurationOverrides, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in cliOverrides)
        {
            if (merged.TryGetValue(pair.Key, out var existing))
            {
                merged[pair.Key] = existing.Merge(pair.Value);
            }
            else
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private static bool TryParseOverrideToken(string raw, out string module, out string? entity, out ValidationError? error)
    {
        module = string.Empty;
        entity = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = ValidationError.Create(
                "moduleFilter.validationOverrides.token.empty",
                "Override token must not be null or whitespace.");
            return false;
        }

        var trimmed = raw.Trim();
        var separator = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
        {
            error = ValidationError.Create(
                "moduleFilter.validationOverrides.token.format",
                $"Override token '{trimmed}' must use Module::Entity format.");
            return false;
        }

        var modulePart = trimmed[..separator].Trim();
        var entityPart = trimmed[(separator + 2)..].Trim();

        if (string.IsNullOrWhiteSpace(modulePart))
        {
            error = ValidationError.Create(
                "moduleFilter.validationOverrides.module.empty",
                $"Override token '{trimmed}' must include a module name.");
            return false;
        }

        module = modulePart;

        if (string.IsNullOrWhiteSpace(entityPart) || string.Equals(entityPart, "*", StringComparison.OrdinalIgnoreCase))
        {
            entity = null;
            return true;
        }

        entity = entityPart;
        return true;
    }

    private sealed class MutableOverride
    {
        public bool AllowMissingPrimaryKeyForAll { get; set; }

        public HashSet<string> AllowMissingPrimaryKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool AllowMissingSchemaForAll { get; set; }

        public HashSet<string> AllowMissingSchema { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
