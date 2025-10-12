using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Osm.Domain.Abstractions;

namespace Osm.Dmm;

public sealed class SsdtProjectDmmLens : IDmmLens<string>
{
    private readonly ScriptDomDmmLens _scriptLens;

    public SsdtProjectDmmLens()
        : this(new ScriptDomDmmLens())
    {
    }

    public SsdtProjectDmmLens(ScriptDomDmmLens scriptLens)
    {
        _scriptLens = scriptLens ?? throw new ArgumentNullException(nameof(scriptLens));
    }

    public Result<IReadOnlyList<DmmTable>> Project(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!Directory.Exists(path))
        {
            return Result<IReadOnlyList<DmmTable>>.Failure(
                ValidationError.Create("dmm.ssdt.path.notFound", $"SSDT project directory '{path}' was not found."));
        }

        var tables = new Dictionary<string, DmmTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(path, "*.sql", SearchOption.AllDirectories))
        {
            using var reader = File.OpenText(file);
            var projection = _scriptLens.Project(reader);
            if (projection.IsFailure)
            {
                return projection;
            }

            foreach (var table in projection.Value)
            {
                var key = $"{table.Schema}.{table.Name}";
                if (tables.TryGetValue(key, out var existing))
                {
                    tables[key] = MergeTables(existing, table);
                }
                else
                {
                    tables[key] = table;
                }
            }
        }

        var ordered = tables.Values
            .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result<IReadOnlyList<DmmTable>>.Success(ordered);
    }

    private static DmmTable MergeTables(DmmTable left, DmmTable right)
    {
        var columns = MergeColumns(left.Columns, right.Columns);
        var primaryKeyColumns = MergePrimaryKeys(left.PrimaryKeyColumns, right.PrimaryKeyColumns);
        var indexes = MergeIndexes(left.Indexes, right.Indexes);
        var foreignKeys = MergeForeignKeys(left.ForeignKeys, right.ForeignKeys);
        var description = left.Description ?? right.Description;

        return new DmmTable(
            left.Schema,
            left.Name,
            columns,
            primaryKeyColumns,
            indexes,
            foreignKeys,
            description);
    }

    private static IReadOnlyList<DmmColumn> MergeColumns(IReadOnlyList<DmmColumn> left, IReadOnlyList<DmmColumn> right)
    {
        var order = new List<DmmColumn>();
        var byName = new Dictionary<string, DmmColumn>(StringComparer.OrdinalIgnoreCase);

        void Upsert(DmmColumn column)
        {
            if (byName.TryGetValue(column.Name, out var existing))
            {
                var defaultExpression = existing.DefaultExpression ?? column.DefaultExpression;
                var collation = existing.Collation ?? column.Collation;
                var description = existing.Description ?? column.Description;
                var merged = new DmmColumn(
                    existing.Name,
                    existing.DataType,
                    existing.IsNullable,
                    defaultExpression,
                    collation,
                    description);
                byName[column.Name] = merged;
                var index = order.FindIndex(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    order[index] = merged;
                }
                return;
            }

            byName[column.Name] = column;
            order.Add(column);
        }

        foreach (var column in left)
        {
            Upsert(column);
        }

        foreach (var column in right)
        {
            Upsert(column);
        }

        return order.ToArray();
    }

    private static IReadOnlyList<string> MergePrimaryKeys(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (left.Count == 0)
        {
            return right.ToArray();
        }

        if (right.Count == 0)
        {
            return left.ToArray();
        }

        if (left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase))
        {
            return left.ToArray();
        }

        var set = new HashSet<string>(left, StringComparer.OrdinalIgnoreCase);
        var combined = new List<string>(left);
        foreach (var column in right)
        {
            if (set.Add(column))
            {
                combined.Add(column);
            }
        }

        return combined.ToArray();
    }

    private static IReadOnlyList<DmmIndex> MergeIndexes(IReadOnlyList<DmmIndex> left, IReadOnlyList<DmmIndex> right)
    {
        var map = new Dictionary<string, DmmIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in left)
        {
            map[index.Name] = index;
        }

        foreach (var index in right)
        {
            if (map.TryGetValue(index.Name, out var existing))
            {
                map[index.Name] = MergeIndex(existing, index);
            }
            else
            {
                map[index.Name] = index;
            }
        }

        return map.Values
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DmmIndex MergeIndex(DmmIndex left, DmmIndex right)
    {
        var keyColumns = left.KeyColumns.Count > 0 ? left.KeyColumns : right.KeyColumns;
        if (left.KeyColumns.Count > 0 && right.KeyColumns.Count > 0)
        {
            keyColumns = left.KeyColumns;
        }

        var includeColumns = left.IncludedColumns.Count > 0 ? left.IncludedColumns : right.IncludedColumns;
        if (left.IncludedColumns.Count > 0 && right.IncludedColumns.Count > 0)
        {
            includeColumns = left.IncludedColumns;
        }

        return new DmmIndex(
            left.Name,
            left.IsUnique || right.IsUnique,
            keyColumns.ToArray(),
            includeColumns.ToArray(),
            left.FilterDefinition ?? right.FilterDefinition,
            left.IsDisabled || right.IsDisabled,
            new DmmIndexOptions(
                left.Options.PadIndex ?? right.Options.PadIndex,
                left.Options.FillFactor ?? right.Options.FillFactor,
                left.Options.IgnoreDuplicateKey ?? right.Options.IgnoreDuplicateKey,
                left.Options.AllowRowLocks ?? right.Options.AllowRowLocks,
                left.Options.AllowPageLocks ?? right.Options.AllowPageLocks,
                left.Options.StatisticsNoRecompute ?? right.Options.StatisticsNoRecompute));
    }

    private static IReadOnlyList<DmmForeignKey> MergeForeignKeys(IReadOnlyList<DmmForeignKey> left, IReadOnlyList<DmmForeignKey> right)
    {
        var map = new Dictionary<string, DmmForeignKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var foreignKey in left)
        {
            map[foreignKey.Name] = foreignKey;
        }

        foreach (var foreignKey in right)
        {
            if (map.TryGetValue(foreignKey.Name, out var existing))
            {
                map[foreignKey.Name] = MergeForeignKey(existing, foreignKey);
            }
            else
            {
                map[foreignKey.Name] = foreignKey;
            }
        }

        return map.Values
            .OrderBy(fk => fk.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DmmForeignKey MergeForeignKey(DmmForeignKey left, DmmForeignKey right)
    {
        return new DmmForeignKey(
            left.Name,
            Choose(left.Column, right.Column),
            Choose(left.ReferencedSchema, right.ReferencedSchema),
            Choose(left.ReferencedTable, right.ReferencedTable),
            Choose(left.ReferencedColumn, right.ReferencedColumn),
            Choose(left.DeleteAction, right.DeleteAction),
            left.IsNotTrusted || right.IsNotTrusted);
    }

    private static string Choose(string left, string right)
        => string.IsNullOrWhiteSpace(left) ? right : left;
}
