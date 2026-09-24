using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Configuration;

public sealed record ModuleValidationOverrideConfiguration(
    IReadOnlyList<string> AllowMissingPrimaryKey,
    bool AllowMissingPrimaryKeyForAll,
    IReadOnlyList<string> AllowMissingSchema,
    bool AllowMissingSchemaForAll)
{
    public static ModuleValidationOverrideConfiguration Empty { get; }
        = new(Array.Empty<string>(), false, Array.Empty<string>(), false);

    public ModuleValidationOverrideConfiguration Merge(ModuleValidationOverrideConfiguration other)
    {
        if (other is null)
        {
            return this;
        }

        var allowMissingPrimaryKeyForAll = AllowMissingPrimaryKeyForAll || other.AllowMissingPrimaryKeyForAll;
        var allowMissingSchemaForAll = AllowMissingSchemaForAll || other.AllowMissingSchemaForAll;

        var primaryKey = allowMissingPrimaryKeyForAll
            ? Array.Empty<string>()
            : AllowMissingPrimaryKey
                .Concat(other.AllowMissingPrimaryKey ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var schema = allowMissingSchemaForAll
            ? Array.Empty<string>()
            : AllowMissingSchema
                .Concat(other.AllowMissingSchema ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new ModuleValidationOverrideConfiguration(
            primaryKey,
            allowMissingPrimaryKeyForAll,
            schema,
            allowMissingSchemaForAll);
    }
}

public sealed record EntityOverrideDefinition(bool AppliesToAll, ImmutableHashSet<string> Entities)
{
    private static readonly ImmutableHashSet<string> EmptySet
        = ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    public static EntityOverrideDefinition None { get; } = new(false, EmptySet);

    public static Result<EntityOverrideDefinition> Create(IEnumerable<string>? rawNames, bool appliesToAll)
    {
        var builder = EmptySet.ToBuilder();
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        if (rawNames is not null)
        {
            foreach (var raw in rawNames)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    errors.Add(ValidationError.Create(
                        "moduleFilter.validationOverrides.entity.empty",
                        "Entity override name must not be null or whitespace."));
                    continue;
                }

                var trimmed = raw.Trim();
                if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
                {
                    appliesToAll = true;
                    continue;
                }

                var entityResult = EntityName.Create(trimmed);
                if (entityResult.IsFailure)
                {
                    foreach (var error in entityResult.Errors)
                    {
                        errors.Add(ValidationError.Create(
                            error.Code,
                            $"Validation override entity '{trimmed}' is invalid: {error.Message}"));
                    }

                    continue;
                }

                builder.Add(entityResult.Value.Value);
            }
        }

        if (errors.Count > 0)
        {
            return Result<EntityOverrideDefinition>.Failure(errors.ToImmutable());
        }

        return new EntityOverrideDefinition(appliesToAll, builder.ToImmutable());
    }

    public EntityOverrideDefinition Merge(EntityOverrideDefinition other)
    {
        if (other is null)
        {
            return this;
        }

        if (AppliesToAll || other.AppliesToAll)
        {
            return new EntityOverrideDefinition(true, EmptySet);
        }

        var merged = Entities.Union(other.Entities, StringComparer.OrdinalIgnoreCase)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        return new EntityOverrideDefinition(false, merged);
    }

    public bool Allows(string entityName)
    {
        if (AppliesToAll)
        {
            return true;
        }

        return Entities.Contains(entityName);
    }
}

public sealed record ModuleValidationOverrideDefinition(
    EntityOverrideDefinition AllowMissingPrimaryKey,
    EntityOverrideDefinition AllowMissingSchema)
{
    public static Result<ModuleValidationOverrideDefinition> Create(ModuleValidationOverrideConfiguration configuration)
    {
        configuration ??= ModuleValidationOverrideConfiguration.Empty;

        var pkResult = EntityOverrideDefinition.Create(
            configuration.AllowMissingPrimaryKey,
            configuration.AllowMissingPrimaryKeyForAll);
        var schemaResult = EntityOverrideDefinition.Create(
            configuration.AllowMissingSchema,
            configuration.AllowMissingSchemaForAll);

        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        if (pkResult.IsFailure)
        {
            errors.AddRange(pkResult.Errors);
        }

        if (schemaResult.IsFailure)
        {
            errors.AddRange(schemaResult.Errors);
        }

        if (errors.Count > 0)
        {
            return Result<ModuleValidationOverrideDefinition>.Failure(errors.ToImmutable());
        }

        return new ModuleValidationOverrideDefinition(pkResult.Value, schemaResult.Value);
    }

    public ModuleValidationOverrideDefinition Merge(ModuleValidationOverrideDefinition other)
    {
        if (other is null)
        {
            return this;
        }

        return new ModuleValidationOverrideDefinition(
            AllowMissingPrimaryKey.Merge(other.AllowMissingPrimaryKey),
            AllowMissingSchema.Merge(other.AllowMissingSchema));
    }
}

public sealed class ModuleValidationOverrides
{
    private readonly ImmutableDictionary<string, ModuleValidationOverrideDefinition> _modules;

    private ModuleValidationOverrides(ImmutableDictionary<string, ModuleValidationOverrideDefinition> modules)
    {
        _modules = modules;
    }

    public static ModuleValidationOverrides Empty { get; }
        = new(ImmutableDictionary.Create<string, ModuleValidationOverrideDefinition>(StringComparer.OrdinalIgnoreCase));

    public bool IsEmpty => _modules.Count == 0;

    public static Result<ModuleValidationOverrides> Create(
        IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration>? configurations)
    {
        if (configurations is null || configurations.Count == 0)
        {
            return Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, ModuleValidationOverrideDefinition>(StringComparer.OrdinalIgnoreCase);
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        foreach (var pair in configurations)
        {
            if (pair.Key is null)
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.validationOverrides.module.null",
                    "Module name for validation overrides must not be null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.validationOverrides.module.empty",
                    "Module name for validation overrides must not be empty or whitespace."));
                continue;
            }

            var trimmed = pair.Key.Trim();
            var moduleResult = ModuleName.Create(trimmed);
            if (moduleResult.IsFailure)
            {
                foreach (var error in moduleResult.Errors)
                {
                    errors.Add(ValidationError.Create(
                        error.Code,
                        $"Validation override module '{trimmed}' is invalid: {error.Message}"));
                }

                continue;
            }

            var definitionResult = ModuleValidationOverrideDefinition.Create(pair.Value ?? ModuleValidationOverrideConfiguration.Empty);
            if (definitionResult.IsFailure)
            {
                errors.AddRange(definitionResult.Errors);
                continue;
            }

            builder[moduleResult.Value.Value] = definitionResult.Value;
        }

        if (errors.Count > 0)
        {
            return Result<ModuleValidationOverrides>.Failure(errors.ToImmutable());
        }

        return new ModuleValidationOverrides(builder.ToImmutable());
    }

    public ModuleValidationOverrides Merge(ModuleValidationOverrides other)
    {
        if (other is null || other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return other;
        }

        var builder = _modules.ToBuilder();
        foreach (var pair in other._modules)
        {
            if (builder.TryGetValue(pair.Key, out var existing))
            {
                builder[pair.Key] = existing.Merge(pair.Value);
            }
            else
            {
                builder[pair.Key] = pair.Value;
            }
        }

        return new ModuleValidationOverrides(builder.ToImmutable());
    }

    public bool AllowsMissingPrimaryKey(string moduleName, string entityName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(entityName))
        {
            return false;
        }

        if (!_modules.TryGetValue(moduleName, out var definition))
        {
            return false;
        }

        return definition.AllowMissingPrimaryKey.Allows(entityName);
    }

    public bool AllowsMissingSchema(string moduleName, string entityName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(entityName))
        {
            return false;
        }

        if (!_modules.TryGetValue(moduleName, out var definition))
        {
            return false;
        }

        return definition.AllowMissingSchema.Allows(entityName);
    }
}
