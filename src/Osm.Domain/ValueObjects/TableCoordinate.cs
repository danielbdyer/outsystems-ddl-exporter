using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;

namespace Osm.Domain.ValueObjects;

public readonly record struct TableCoordinate(ModuleName? Module, SchemaName Schema, TableName Table)
{
    public TableCoordinate(SchemaName schema, TableName table)
        : this(null, schema, table)
    {
    }

    public override string ToString()
    {
        var schemaTable = $"{Schema.Value}.{Table.Value}";
        return Module is { } module ? $"{module.Value}::{schemaTable}" : schemaTable;
    }

    public TableCoordinate WithoutModule() => new(null, Schema, Table);

    public bool Equals(TableCoordinate other)
        => OrdinalIgnoreCaseComparer.Equals(this, other);

    public override int GetHashCode()
        => OrdinalIgnoreCaseComparer.GetHashCode(this);

    public static IEqualityComparer<TableCoordinate> OrdinalIgnoreCaseComparer { get; } = new OrdinalIgnoreCaseEqualityComparer();

    public static IEqualityComparer<TableCoordinate> ModuleScopedComparer { get; } = new ModuleScopedEqualityComparer();

    public static Result<TableCoordinate> Create(string? schema, string? table)
        => Create(module: null, schema, table);

    public static Result<TableCoordinate> Create(string? module, string? schema, string? table)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        ModuleName? moduleName = null;
        if (!string.IsNullOrWhiteSpace(module))
        {
            var moduleResult = ModuleName.Create(module);
            if (moduleResult.IsFailure)
            {
                errors.AddRange(moduleResult.Errors);
            }
            else
            {
                moduleName = moduleResult.Value;
            }
        }

        var schemaResult = SchemaName.Create(schema);
        if (schemaResult.IsFailure)
        {
            errors.AddRange(schemaResult.Errors);
        }

        var tableResult = TableName.Create(table);
        if (tableResult.IsFailure)
        {
            errors.AddRange(tableResult.Errors);
        }

        if (errors.Count > 0)
        {
            return Result<TableCoordinate>.Failure(errors.ToImmutable());
        }

        return new TableCoordinate(moduleName, schemaResult.Value, tableResult.Value);
    }

    public static TableCoordinate From(EntityModel entity, bool includeModule = false)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        return includeModule
            ? new TableCoordinate(entity.Module, entity.Schema, entity.PhysicalName)
            : new TableCoordinate(entity.Schema, entity.PhysicalName);
    }

    private sealed class OrdinalIgnoreCaseEqualityComparer : IEqualityComparer<TableCoordinate>
    {
        public bool Equals(TableCoordinate x, TableCoordinate y)
        {
            return string.Equals(x.Schema.Value, y.Schema.Value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table.Value, y.Table.Value, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(TableCoordinate obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Schema.Value, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Table.Value, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed class ModuleScopedEqualityComparer : IEqualityComparer<TableCoordinate>
    {
        public bool Equals(TableCoordinate x, TableCoordinate y)
        {
            return string.Equals(x.Module?.Value, y.Module?.Value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Schema.Value, y.Schema.Value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table.Value, y.Table.Value, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(TableCoordinate obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Module?.Value, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Schema.Value, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Table.Value, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
