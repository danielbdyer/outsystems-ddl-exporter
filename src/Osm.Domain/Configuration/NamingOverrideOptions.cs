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

    private readonly IReadOnlyDictionary<TableCoordinate, TableName> _tableOverrides;
    private readonly IReadOnlyDictionary<string, TableName> _entityOverrides;

    private NamingOverrideOptions(
        IReadOnlyDictionary<TableCoordinate, TableName> tableOverrides,
        IReadOnlyDictionary<string, TableName> entityOverrides)
    {
        _tableOverrides = tableOverrides;
        _entityOverrides = entityOverrides;
    }

    public static NamingOverrideOptions Empty { get; } = new(
        new Dictionary<TableCoordinate, TableName>(TableCoordinate.OrdinalIgnoreCaseComparer),
        new Dictionary<string, TableName>(Comparer));

    public bool IsEmpty => _tableOverrides.Count == 0 && _entityOverrides.Count == 0;

    public IReadOnlyDictionary<TableCoordinate, TableName> TableOverrides => _tableOverrides;

    public static Result<NamingOverrideOptions> Create(IEnumerable<NamingOverrideRule>? overrides)
    {
        if (overrides is null)
        {
            return Empty;
        }

        var tableDictionary = new Dictionary<TableCoordinate, TableName>(TableCoordinate.OrdinalIgnoreCaseComparer);
        var entityDictionary = new Dictionary<string, TableName>(Comparer);

        foreach (var rule in overrides)
        {
            if (rule is null)
            {
                return ValidationError.Create("namingOverride.rule.null", "Naming override rule cannot be null.");
            }

            if (rule.PhysicalName is not null)
            {
                var coordinate = new TableCoordinate(rule.Schema!.Value, rule.PhysicalName.Value);
                if (tableDictionary.TryGetValue(coordinate, out var existing) &&
                    !Comparer.Equals(existing.Value, rule.Target.Value))
                {
                    return ValidationError.Create(
                        "namingOverride.duplicate",
                        $"Multiple overrides provided for {coordinate}.");
                }

                tableDictionary[coordinate] = rule.Target;
            }

            if (rule.LogicalName is not null)
            {
                var entityKey = EntityKey(rule.Module?.Value, rule.LogicalName.Value.Value);
                if (entityDictionary.TryGetValue(entityKey, out var existing) &&
                    !Comparer.Equals(existing.Value, rule.Target.Value))
                {
                    return ValidationError.Create(
                        "namingOverride.entity.duplicate",
                        $"Multiple overrides provided for logical entity {rule.LogicalName.Value}.");
                }

                entityDictionary[entityKey] = rule.Target;
            }
        }

        return new NamingOverrideOptions(tableDictionary, entityDictionary);
    }

    public NamingOverrideOptions MergeWith(IEnumerable<NamingOverrideRule> overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        if (!overrides.Any())
        {
            return this;
        }

        var mergedTables = new Dictionary<TableCoordinate, TableName>(_tableOverrides, TableCoordinate.OrdinalIgnoreCaseComparer);
        var mergedEntities = new Dictionary<string, TableName>(_entityOverrides, Comparer);

        foreach (var rule in overrides)
        {
            if (rule is null)
            {
                continue;
            }

            if (rule.PhysicalName is not null)
            {
                var coordinate = new TableCoordinate(rule.Schema!.Value, rule.PhysicalName.Value);
                mergedTables[coordinate] = rule.Target;
            }

            if (rule.LogicalName is not null)
            {
                var entityKey = EntityKey(rule.Module?.Value, rule.LogicalName.Value.Value);
                mergedEntities[entityKey] = rule.Target;
            }
        }

        return new NamingOverrideOptions(mergedTables, mergedEntities);
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

        var coordinateResult = TableCoordinate.Create(schema, table);
        if (coordinateResult.IsSuccess &&
            _tableOverrides.TryGetValue(coordinateResult.Value, out var value))
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

    public bool TryGetModuleScopedEntityOverride(string module, string logicalName, out TableName overrideName)
    {
        if (module is null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        if (logicalName is null)
        {
            throw new ArgumentNullException(nameof(logicalName));
        }

        var key = EntityKey(module, logicalName);
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

    private static string EntityKey(string? module, string logicalName)
    {
        var modulePart = string.IsNullOrWhiteSpace(module) ? string.Empty : module!.Trim();
        return $"{modulePart}::{logicalName}";
    }
}

public sealed record NamingOverrideRule(
    SchemaName? Schema,
    TableName? PhysicalName,
    ModuleName? Module,
    EntityName? LogicalName,
    TableName Target)
{
    public static Result<NamingOverrideRule> Create(
        string? schema,
        string? table,
        string? module,
        string? logicalName,
        string? target)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        SchemaName? schemaName = null;
        TableName? physicalName = null;
        ModuleName? moduleName = null;
        EntityName? entityName = null;

        if (!string.IsNullOrWhiteSpace(table) || !string.IsNullOrWhiteSpace(schema))
        {
            if (string.IsNullOrWhiteSpace(table))
            {
                errors.Add(ValidationError.Create(
                    "namingOverride.rule.table.missing",
                    "Physical overrides must include a table name."));
            }
            else
            {
                var tableResult = TableName.Create(table);
                if (tableResult.IsFailure)
                {
                    errors.AddRange(tableResult.Errors);
                }
                else
                {
                    physicalName = tableResult.Value;
                }
            }

            var schemaValue = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema!.Trim();
            var schemaResult = SchemaName.Create(schemaValue);
            if (schemaResult.IsFailure)
            {
                errors.AddRange(schemaResult.Errors);
            }
            else
            {
                schemaName = schemaResult.Value;
            }
        }

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

        if (!string.IsNullOrWhiteSpace(logicalName))
        {
            var logicalResult = EntityName.Create(logicalName);
            if (logicalResult.IsFailure)
            {
                errors.AddRange(logicalResult.Errors);
            }
            else
            {
                entityName = logicalResult.Value;
            }
        }

        if (moduleName is not null && entityName is null)
        {
            errors.Add(ValidationError.Create(
                "namingOverride.rule.entity.missing",
                "Module-scoped overrides must include an entity name."));
        }

        if (physicalName is null && entityName is null)
        {
            errors.Add(ValidationError.Create(
                "namingOverride.rule.scope.missing",
                "Naming overrides must target a physical table, a logical entity, or both."));
        }

        var targetResult = TableName.Create(target);
        if (targetResult.IsFailure)
        {
            errors.AddRange(targetResult.Errors);
        }

        if (errors.Count > 0)
        {
            return Result<NamingOverrideRule>.Failure(errors.ToImmutable());
        }

        if (physicalName is not null && schemaName is null)
        {
            // Ensure schema defaulting is applied when only a table name was supplied.
            schemaName = SchemaName.Create("dbo").Value;
        }

        return new NamingOverrideRule(schemaName, physicalName, moduleName, entityName, targetResult.Value);
    }
}
