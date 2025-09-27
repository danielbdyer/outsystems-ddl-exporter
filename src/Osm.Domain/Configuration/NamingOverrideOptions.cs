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

    private NamingOverrideOptions(IReadOnlyDictionary<string, TableName> tableOverrides)
    {
        _tableOverrides = tableOverrides;
    }

    public static NamingOverrideOptions Empty { get; } = new(new Dictionary<string, TableName>(Comparer));

    public bool IsEmpty => _tableOverrides.Count == 0;

    public IReadOnlyDictionary<string, TableName> TableOverrides => _tableOverrides;

    public static Result<NamingOverrideOptions> Create(IEnumerable<TableNamingOverride>? overrides)
    {
        if (overrides is null)
        {
            return Empty;
        }

        var dictionary = new Dictionary<string, TableName>(Comparer);
        foreach (var tableOverride in overrides)
        {
            if (tableOverride is null)
            {
                return ValidationError.Create("namingOverride.null", "Table override cannot be null.");
            }

            var key = Key(tableOverride.Schema.Value, tableOverride.Source.Value);
            if (dictionary.TryGetValue(key, out var existing) && !Comparer.Equals(existing.Value, tableOverride.Target.Value))
            {
                return ValidationError.Create(
                    "namingOverride.duplicate",
                    $"Multiple overrides provided for {tableOverride.Schema.Value}.{tableOverride.Source.Value}.");
            }

            dictionary[key] = tableOverride.Target;
        }

        if (dictionary.Count == 0)
        {
            return Empty;
        }

        return new NamingOverrideOptions(dictionary);
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

            var key = Key(tableOverride.Schema.Value, tableOverride.Source.Value);
            merged[key] = tableOverride.Target;
        }

        return new NamingOverrideOptions(merged);
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

        var key = Key(schema, table);
        if (_tableOverrides.TryGetValue(key, out var value))
        {
            overrideName = value.Value;
            return true;
        }

        overrideName = string.Empty;
        return false;
    }

    public string GetEffectiveTableName(string schema, string table, string? logicalName = null)
    {
        if (TryGetTableOverride(schema, table, out var overrideName))
        {
            return overrideName;
        }

        if (!string.IsNullOrWhiteSpace(logicalName))
        {
            return logicalName!.Trim();
        }

        return table;
    }

    private static string Key(string schema, string table) => $"{schema}.{table}";
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
