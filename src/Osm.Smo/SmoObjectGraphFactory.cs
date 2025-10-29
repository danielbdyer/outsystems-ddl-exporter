using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;

namespace Osm.Smo;

public sealed class SmoObjectGraphFactory : IDisposable
{
    private readonly SmoContext _context;
    private readonly Dictionary<string, Database> _databaseLookup;
    private readonly bool _ownsContext;
    private bool _disposed;

    public SmoObjectGraphFactory()
        : this(new SmoContext(), ownsContext: true)
    {
    }

    public SmoObjectGraphFactory(SmoContext context)
        : this(context, ownsContext: false)
    {
    }

    private SmoObjectGraphFactory(SmoContext context, bool ownsContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _databaseLookup = new Dictionary<string, Database>(StringComparer.OrdinalIgnoreCase)
        {
            [context.DatabaseName] = context.Database
        };
        _ownsContext = ownsContext;
    }

    public ImmutableArray<Table> CreateTables(SmoModel model, SmoBuildOptions? options = null)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        options ??= SmoBuildOptions.Default;

        if (model.Tables.IsDefaultOrEmpty)
        {
            return ImmutableArray<Table>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<Table>(model.Tables.Length);
        foreach (var table in model.Tables)
        {
            builder.Add(CreateTable(table, options));
        }

        return builder.MoveToImmutable();
    }

    public Table CreateTable(SmoTableDefinition table, SmoBuildOptions? options = null)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        options ??= SmoBuildOptions.Default;

        var database = ResolveDatabase(table.Catalog, options.DefaultCatalogName);
        var effectiveName = options.NamingOverrides.GetEffectiveTableName(
            table.Schema,
            table.Name,
            table.LogicalName,
            table.OriginalModule);

        var smoTable = new Table(database, effectiveName, table.Schema)
        {
            AnsiNullsStatus = true,
            QuotedIdentifierStatus = true,
        };

        AddTableDescription(smoTable, table.Description);
        AddColumns(smoTable, table.Columns);
        AddIndexes(smoTable, table.Indexes);
        AddForeignKeys(smoTable, table.ForeignKeys, options.NamingOverrides);

        return smoTable;
    }

    private Database ResolveDatabase(string? catalog, string fallbackCatalog)
    {
        var resolvedCatalog = string.IsNullOrWhiteSpace(catalog)
            ? (string.IsNullOrWhiteSpace(fallbackCatalog) ? _context.DatabaseName : fallbackCatalog)
            : catalog!;

        if (_databaseLookup.TryGetValue(resolvedCatalog, out var database))
        {
            return database;
        }

        database = string.Equals(resolvedCatalog, _context.DatabaseName, StringComparison.OrdinalIgnoreCase)
            ? _context.Database
            : new Database(_context.Server, resolvedCatalog);

        _databaseLookup[resolvedCatalog] = database;
        return database;
    }

    private static void AddTableDescription(Table table, string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        var property = new ExtendedProperty(table, "MS_Description")
        {
            Value = description
        };

        table.ExtendedProperties.Add(property);
    }

    private static void AddColumns(Table table, ImmutableArray<SmoColumnDefinition> columns)
    {
        foreach (var column in columns)
        {
            var smoColumn = new Column(table, column.Name, column.DataType)
            {
                Nullable = column.Nullable,
                Identity = column.IsIdentity,
                IdentitySeed = column.IsIdentity ? column.IdentitySeed : 0,
                IdentityIncrement = column.IsIdentity ? column.IdentityIncrement : 0,
                Computed = column.IsComputed,
                ComputedText = column.IsComputed ? column.ComputedExpression : null,
                Collation = column.Collation,
            };

            if (!string.IsNullOrWhiteSpace(column.Description))
            {
                var description = new ExtendedProperty(smoColumn, "MS_Description")
                {
                    Value = column.Description
                };

                smoColumn.ExtendedProperties.Add(description);
            }

            table.Columns.Add(smoColumn);
        }
    }

    private static void AddIndexes(Table table, ImmutableArray<SmoIndexDefinition> indexes)
    {
        foreach (var index in indexes)
        {
            var smoIndex = new Microsoft.SqlServer.Management.Smo.Index(table, index.Name)
            {
                IndexKeyType = ResolveIndexKeyType(index),
                IsUnique = index.IsUnique,
                FilterDefinition = index.Metadata.FilterDefinition,
                IgnoreDuplicateKeys = index.Metadata.IgnoreDuplicateKey,
                PadIndex = index.Metadata.IsPadded,
                DisallowRowLocks = !index.Metadata.AllowRowLocks,
                DisallowPageLocks = !index.Metadata.AllowPageLocks,
                NoAutomaticRecomputation = index.Metadata.StatisticsNoRecompute,
            };

            if (index.Metadata.FillFactor.HasValue)
            {
                smoIndex.FillFactor = (byte)index.Metadata.FillFactor.Value;
            }

            if (index.Metadata.DataSpace is { } dataSpace && !string.IsNullOrWhiteSpace(dataSpace.Name))
            {
                smoIndex.FileGroup = dataSpace.Name;
            }

            foreach (var column in index.Columns.OrderBy(static c => c.Ordinal))
            {
                var indexedColumn = new IndexedColumn(smoIndex, column.Name)
                {
                    Descending = column.IsDescending,
                    IsIncluded = column.IsIncluded,
                };

                smoIndex.IndexedColumns.Add(indexedColumn);
            }

            if (!string.IsNullOrWhiteSpace(index.Description))
            {
                var description = new ExtendedProperty(smoIndex, "MS_Description")
                {
                    Value = index.Description
                };

                smoIndex.ExtendedProperties.Add(description);
            }

            table.Indexes.Add(smoIndex);
        }
    }

    private static IndexKeyType ResolveIndexKeyType(SmoIndexDefinition index)
    {
        if (index.IsPrimaryKey)
        {
            return IndexKeyType.DriPrimaryKey;
        }

        if (index.IsUnique)
        {
            return IndexKeyType.DriUniqueKey;
        }

        return IndexKeyType.None;
    }

    private static void AddForeignKeys(
        Table table,
        ImmutableArray<SmoForeignKeyDefinition> foreignKeys,
        NamingOverrideOptions namingOverrides)
    {
        foreach (var foreignKey in foreignKeys)
        {
            var referencedTable = namingOverrides.GetEffectiveTableName(
                foreignKey.ReferencedSchema,
                foreignKey.ReferencedTable,
                foreignKey.ReferencedLogicalTable,
                foreignKey.ReferencedModule);

            var smoForeignKey = new ForeignKey(table, foreignKey.Name)
            {
                ReferencedTable = referencedTable,
                ReferencedTableSchema = foreignKey.ReferencedSchema,
                DeleteAction = foreignKey.DeleteAction,
                IsChecked = !foreignKey.IsNoCheck,
            };

            var pairCount = Math.Min(foreignKey.Columns.Length, foreignKey.ReferencedColumns.Length);
            for (var index = 0; index < pairCount; index++)
            {
                var columnName = foreignKey.Columns[index];
                var referencedColumn = foreignKey.ReferencedColumns[index];

                if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(referencedColumn))
                {
                    continue;
                }

                smoForeignKey.Columns.Add(new ForeignKeyColumn(smoForeignKey, columnName, referencedColumn));
            }

            if (smoForeignKey.Columns.Count == 0 &&
                foreignKey.Columns.Length > 0 &&
                foreignKey.ReferencedColumns.Length > 0 &&
                !string.IsNullOrWhiteSpace(foreignKey.Columns[0]) &&
                !string.IsNullOrWhiteSpace(foreignKey.ReferencedColumns[0]))
            {
                smoForeignKey.Columns.Add(new ForeignKeyColumn(
                    smoForeignKey,
                    foreignKey.Columns[0],
                    foreignKey.ReferencedColumns[0]));
            }
            table.ForeignKeys.Add(smoForeignKey);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsContext)
        {
            _context.Dispose();
        }

        _disposed = true;
    }
}
