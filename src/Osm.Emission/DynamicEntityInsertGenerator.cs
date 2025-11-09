using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Smo;

namespace Osm.Emission;

public sealed record DynamicEntityDataset(ImmutableArray<StaticEntityTableData> Tables)
{
    public static DynamicEntityDataset Empty { get; } = new(ImmutableArray<StaticEntityTableData>.Empty);

    public bool IsEmpty => Tables.IsDefaultOrEmpty || Tables.Length == 0;

    public static DynamicEntityDataset Create(IEnumerable<StaticEntityTableData> tables)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var materialized = tables.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return Empty;
        }

        return new DynamicEntityDataset(materialized);
    }
}

public sealed class DynamicEntityInsertGenerationOptions
{
    public static DynamicEntityInsertGenerationOptions Default { get; } = new(1000);

    public DynamicEntityInsertGenerationOptions(int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        BatchSize = batchSize;
    }

    public int BatchSize { get; }
}

public sealed record DynamicEntityInsertScript(StaticEntitySeedTableDefinition Definition, string Script);

public sealed class DynamicEntityInsertGenerator
{
    private readonly SqlLiteralFormatter _literalFormatter;

    public DynamicEntityInsertGenerator(SqlLiteralFormatter literalFormatter)
    {
        _literalFormatter = literalFormatter ?? throw new ArgumentNullException(nameof(literalFormatter));
    }

    public ImmutableArray<DynamicEntityInsertScript> GenerateScripts(
        DynamicEntityDataset dataset,
        ImmutableArray<StaticEntityTableData> staticSeedCatalog,
        DynamicEntityInsertGenerationOptions? options = null)
    {
        if (dataset is null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        if (staticSeedCatalog.IsDefault)
        {
            staticSeedCatalog = ImmutableArray<StaticEntityTableData>.Empty;
        }

        options ??= DynamicEntityInsertGenerationOptions.Default;

        if (dataset.IsEmpty)
        {
            return ImmutableArray<DynamicEntityInsertScript>.Empty;
        }

        var staticIndex = BuildStaticIndex(staticSeedCatalog);
        var orderedTables = dataset.Tables
            .Where(static table => table is not null)
            .OrderBy(table => table.Definition.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(table => table.Definition.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(table => table.Definition.EffectiveName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scripts = ImmutableArray.CreateBuilder<DynamicEntityInsertScript>(orderedTables.Length);

        foreach (var table in orderedTables)
        {
            var filteredRows = FilterRows(table, staticIndex);
            if (filteredRows.Length == 0)
            {
                continue;
            }

            var normalized = StaticEntitySeedDeterminizer.Normalize(new[] { new StaticEntityTableData(table.Definition, filteredRows) });
            if (normalized.IsDefaultOrEmpty)
            {
                continue;
            }

            var normalizedTable = normalized[0];
            var script = BuildScript(normalizedTable.Definition, normalizedTable.Rows, options.BatchSize);
            scripts.Add(new DynamicEntityInsertScript(normalizedTable.Definition, script));
        }

        return scripts.ToImmutable();
    }

    private static ImmutableArray<StaticEntityRow> FilterRows(
        StaticEntityTableData table,
        Dictionary<TableKey, HashSet<string>> staticIndex)
    {
        var definition = table.Definition;
        var key = CreateTableKey(definition);
        staticIndex.TryGetValue(key, out var staticRows);

        var primaryIndices = GetPrimaryIndices(definition);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<StaticEntityRow>();

        foreach (var row in table.Rows)
        {
            var hash = ComputeRowKey(row, primaryIndices);
            if (hash is null)
            {
                continue;
            }

            if (staticRows is not null && staticRows.Contains(hash))
            {
                continue;
            }

            if (seen.Add(hash))
            {
                builder.Add(row);
            }
        }

        return builder.ToImmutable();
    }

    private static Dictionary<TableKey, HashSet<string>> BuildStaticIndex(ImmutableArray<StaticEntityTableData> tables)
    {
        var index = new Dictionary<TableKey, HashSet<string>>(TableKeyComparer.Instance);
        if (tables.IsDefaultOrEmpty)
        {
            return index;
        }

        foreach (var table in tables)
        {
            var key = CreateTableKey(table.Definition);
            if (!index.TryGetValue(key, out var rows))
            {
                rows = new HashSet<string>(StringComparer.Ordinal);
                index[key] = rows;
            }

            var primaryIndices = GetPrimaryIndices(table.Definition);
            foreach (var row in table.Rows)
            {
                var hash = ComputeRowKey(row, primaryIndices);
                if (hash is not null)
                {
                    rows.Add(hash);
                }
            }
        }

        return index;
    }

    private static int[] GetPrimaryIndices(StaticEntitySeedTableDefinition definition)
    {
        var indices = new List<int>();
        for (var i = 0; i < definition.Columns.Length; i++)
        {
            if (definition.Columns[i].IsPrimaryKey)
            {
                indices.Add(i);
            }
        }

        if (indices.Count == 0)
        {
            for (var i = 0; i < definition.Columns.Length; i++)
            {
                indices.Add(i);
            }
        }

        return indices.ToArray();
    }

    private static string? ComputeRowKey(StaticEntityRow row, IReadOnlyList<int> primaryIndices)
    {
        if (row.Values.IsDefault)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var index in primaryIndices)
        {
            if (index >= row.Values.Length)
            {
                builder.Append('\u001f');
                continue;
            }

            var value = row.Values[index];
            builder.Append(value switch
            {
                null => "<null>",
                DateTime dt => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeOnly time => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            });
            builder.Append('\u001f');
        }

        return builder.ToString();
    }

    private string BuildScript(
        StaticEntitySeedTableDefinition definition,
        ImmutableArray<StaticEntityRow> rows,
        int batchSize)
    {
        var builder = new StringBuilder();
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine($"-- Module: {definition.Module}");
        builder.AppendLine($"-- Entity: {definition.LogicalName} ({definition.Schema}.{definition.PhysicalName})");
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine();
        builder.AppendLine("SET NOCOUNT ON;");
        builder.AppendLine();

        var targetIdentifier = SqlIdentifierFormatter.Qualify(definition.Schema, definition.EffectiveName);
        var columnNames = definition.Columns
            .Select(column => SqlIdentifierFormatter.Quote(column.EffectiveColumnName))
            .ToArray();

        var hasIdentity = definition.Columns.Any(column => column.IsIdentity);
        if (hasIdentity)
        {
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} ON;");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        var batches = PartitionRows(rows, batchSize).ToArray();
        for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            var batchRows = batches[batchIndex];
            builder.AppendLine($"PRINT 'Applying batch {batchIndex + 1} for {targetIdentifier} ({batchRows.Length} rows)';");
            builder.AppendLine($"INSERT INTO {targetIdentifier} WITH (TABLOCK, CHECK_CONSTRAINTS)");
            builder.Append("    (");
            builder.Append(string.Join(", ", columnNames));
            builder.AppendLine(")");
            builder.AppendLine("VALUES");

            for (var rowIndex = 0; rowIndex < batchRows.Length; rowIndex++)
            {
                var row = batchRows[rowIndex];
                builder.Append("    (");
                for (var columnIndex = 0; columnIndex < definition.Columns.Length; columnIndex++)
                {
                    if (columnIndex > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(_literalFormatter.FormatValue(row.Values[columnIndex]));
                }

                builder.Append(')');
                if (rowIndex < batchRows.Length - 1)
                {
                    builder.Append(',');
                }

                builder.AppendLine();
            }

            builder.AppendLine("GO");
            builder.AppendLine();
        }

        if (hasIdentity)
        {
            builder.AppendLine($"SET IDENTITY_INSERT {targetIdentifier} OFF;");
            builder.AppendLine("GO");
        }

        return builder.ToString();
    }

    private static IEnumerable<ImmutableArray<StaticEntityRow>> PartitionRows(
        ImmutableArray<StaticEntityRow> rows,
        int batchSize)
    {
        if (rows.IsDefaultOrEmpty)
        {
            yield break;
        }

        for (var i = 0; i < rows.Length; i += batchSize)
        {
            var length = Math.Min(batchSize, rows.Length - i);
            yield return rows.Skip(i).Take(length).ToImmutableArray();
        }
    }

    private sealed record TableKey(string Module, string Schema, string Table);

    private sealed class TableKeyComparer : IEqualityComparer<TableKey>
    {
        public static TableKeyComparer Instance { get; } = new();

        public bool Equals(TableKey? x, TableKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Module, y.Module, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(TableKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Module ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table ?? string.Empty));
        }
    }

    private static TableKey CreateTableKey(StaticEntitySeedTableDefinition definition)
        => new(definition.Module ?? string.Empty, definition.Schema ?? string.Empty, definition.PhysicalName ?? string.Empty);
}
