using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Configuration;

public sealed record NamingOverrideOptions
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly IReadOnlyDictionary<string, TableName> _tableOverrides;
    private readonly IReadOnlyDictionary<string, TableName> _entityOverrides;

    private NamingOverrideOptions(
        IReadOnlyDictionary<string, TableName> tableOverrides,
        IReadOnlyDictionary<string, TableName> entityOverrides)
    {
        _tableOverrides = tableOverrides;
        _entityOverrides = entityOverrides;
    }

    public static NamingOverrideOptions Empty { get; } = new(
        new Dictionary<string, TableName>(Comparer),
        new Dictionary<string, TableName>(Comparer));

    public bool IsEmpty => _tableOverrides.Count == 0 && _entityOverrides.Count == 0;

    public IReadOnlyDictionary<string, TableName> TableOverrides => _tableOverrides;

    public static Result<NamingOverrideOptions> Create(
        IEnumerable<TableNamingOverride>? tableOverrides,
        IEnumerable<EntityNamingOverride>? entityOverrides = null)
    {
        var tableDictionary = new Dictionary<string, TableName>(Comparer);
        if (tableOverrides is not null)
        {
            foreach (var tableOverride in tableOverrides)
            {
                if (tableOverride is null)
                {
                    return ValidationError.Create("namingOverride.null", "Table override cannot be null.");
                }

                var key = TableKey(tableOverride.Schema.Value, tableOverride.Source.Value);
                if (tableDictionary.TryGetValue(key, out var existing) &&
                    !Comparer.Equals(existing.Value, tableOverride.Target.Value))
                {
                    return ValidationError.Create(
                        "namingOverride.duplicate",
                        $"Multiple overrides provided for {tableOverride.Schema.Value}.{tableOverride.Source.Value}.");
                }

                tableDictionary[key] = tableOverride.Target;
            }
        }

        var entityDictionary = new Dictionary<string, TableName>(Comparer);
        if (entityOverrides is not null)
        {
            foreach (var entityOverride in entityOverrides)
            {
                if (entityOverride is null)
                {
                    return ValidationError.Create("namingOverride.entity.null", "Entity override cannot be null.");
                }

                var key = EntityKey(entityOverride.Module?.Value, entityOverride.LogicalName.Value);
                if (entityDictionary.TryGetValue(key, out var existing) &&
                    !Comparer.Equals(existing.Value, entityOverride.Target.Value))
                {
                    return ValidationError.Create(
                        "namingOverride.entity.duplicate",
                        $"Multiple overrides provided for logical entity {entityOverride.LogicalName.Value}.");
                }

                entityDictionary[key] = entityOverride.Target;
            }
        }

        return new NamingOverrideOptions(tableDictionary, entityDictionary);
    }

    public NamingOverrideOptions MergeWith(IEnumerable<TableNamingOverride> overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        if (!overrides.Any())
        {
            return this;
        }

        var merged = new Dictionary<string, TableName>(_tableOverrides, Comparer);
        foreach (var tableOverride in overrides)
        {
            if (tableOverride is null)
            {
                continue;
            }

            var key = TableKey(tableOverride.Schema.Value, tableOverride.Source.Value);
            merged[key] = tableOverride.Target;
        }

        return new NamingOverrideOptions(merged, _entityOverrides);
    }

    public bool TryGetTableOverride(string schema, string table, out string overrideName)
    {
        if (schema is null)
        {
            throw new ArgumentNullException(nameof(schema));
        }

        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        var key = TableKey(schema, table);
        if (_tableOverrides.TryGetValue(key, out var value))
        {
            overrideName = value.Value;
            return true;
        }

        overrideName = string.Empty;
        return false;
    }

    public bool TryGetEntityOverride(string? module, string logicalName, out TableName overrideName)
    {
        if (logicalName is null)
        {
            throw new ArgumentNullException(nameof(logicalName));
        }

        if (!string.IsNullOrWhiteSpace(module))
        {
            var scopedKey = EntityKey(module!, logicalName);
            if (_entityOverrides.TryGetValue(scopedKey, out var scopedValue))
            {
                overrideName = scopedValue;
                return true;
            }
        }

        var key = EntityKey(null, logicalName);
        if (_entityOverrides.TryGetValue(key, out var value))
        {
            overrideName = value;
            return true;
        }

        overrideName = default;
        return false;
    }

    public string GetEffectiveTableName(string schema, string table, string? logicalName = null, string? module = null)
    {
        if (TryGetTableOverride(schema, table, out var overrideName))
        {
            return overrideName;
        }

        if (!string.IsNullOrWhiteSpace(logicalName))
        {
            if (TryGetEntityOverride(module, logicalName!, out var entityOverride))
            {
                return entityOverride.Value;
            }

            return logicalName!.Trim();
        }

        return table;
    }

    private static string TableKey(string schema, string table) => $"{schema}.{table}";

    private static string EntityKey(string? module, string logicalName)
    {
        var modulePart = string.IsNullOrWhiteSpace(module) ? string.Empty : module!.Trim();
        return $"{modulePart}::{logicalName}";
    }
}

public sealed record TableNamingOverride(SchemaName Schema, TableName Source, TableName Target)
{
    public static Result<TableNamingOverride> Create(string? schema, string? source, string? target)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        var schemaValue = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema!.Trim();

        var schemaResult = SchemaName.Create(schemaValue);
        if (schemaResult.IsFailure)
        {
            errors.AddRange(schemaResult.Errors);
        }

        var sourceResult = TableName.Create(source);
        if (sourceResult.IsFailure)
        {
            errors.AddRange(sourceResult.Errors);
        }

        var targetResult = TableName.Create(target);
        if (targetResult.IsFailure)
        {
            errors.AddRange(targetResult.Errors);
        }

        if (errors.Count > 0)
        {
            return Result<TableNamingOverride>.Failure(errors.ToImmutable());
        }

        return new TableNamingOverride(schemaResult.Value, sourceResult.Value, targetResult.Value);
    }
}

public sealed record EntityNamingOverride(ModuleName? Module, EntityName LogicalName, TableName Target)
{
    public static Result<EntityNamingOverride> Create(string? module, string? logicalName, string? target)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        ModuleName? moduleName = null;
        if (!string.IsNullOrWhiteSpace(module))
        {
            var moduleResult = ModuleName.Create(module!.Trim());
            if (moduleResult.IsFailure)
            {
                errors.AddRange(moduleResult.Errors);
            }
            else
            {
                moduleName = moduleResult.Value;
            }
        }

        var logicalResult = EntityName.Create(logicalName);
        if (logicalResult.IsFailure)
        {
            errors.AddRange(logicalResult.Errors);
        }

        var targetResult = TableName.Create(target);
        if (targetResult.IsFailure)
        {
            errors.AddRange(targetResult.Errors);
        }

        if (errors.Count > 0)
        {
            return Result<EntityNamingOverride>.Failure(errors.ToImmutable());
        }

        return new EntityNamingOverride(moduleName, logicalResult.Value, targetResult.Value);
    }
}
