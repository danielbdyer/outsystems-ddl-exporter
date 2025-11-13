using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Domain.Model;
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
    private static readonly object _missingValue = new();
    private readonly SqlLiteralFormatter _literalFormatter;

    public DynamicEntityInsertGenerator(SqlLiteralFormatter literalFormatter)
    {
        _literalFormatter = literalFormatter ?? throw new ArgumentNullException(nameof(literalFormatter));
    }

    public ImmutableArray<DynamicEntityInsertScript> GenerateScripts(
        DynamicEntityDataset dataset,
        ImmutableArray<StaticEntityTableData> staticSeedCatalog,
        DynamicEntityInsertGenerationOptions? options = null,
        OsmModel? model = null)
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

        var orderedTables = EntityDependencySorter.SortByForeignKeys(dataset.Tables, model).Tables;
        if (orderedTables.IsDefaultOrEmpty)
        {
            return ImmutableArray<DynamicEntityInsertScript>.Empty;
        }

        var scripts = ImmutableArray.CreateBuilder<DynamicEntityInsertScript>(orderedTables.Length);

        foreach (var table in orderedTables)
        {
            var filteredRows = FilterRows(table);
            if (filteredRows.Length == 0)
            {
                continue;
            }

            var normalized = EntitySeedDeterminizer.Normalize(new[] { new StaticEntityTableData(table.Definition, filteredRows) });
            if (normalized.IsDefaultOrEmpty)
            {
                continue;
            }

            var normalizedTable = normalized[0];
            var orderedRows = OrderRows(normalizedTable.Definition, normalizedTable.Rows, model);
            var script = BuildScript(normalizedTable.Definition, orderedRows, options.BatchSize);
            scripts.Add(new DynamicEntityInsertScript(normalizedTable.Definition, script));
        }

        var materialized = scripts.ToImmutable();
        return ApplyDependencyOrdering(materialized, model);
    }

    private static ImmutableArray<StaticEntityRow> FilterRows(
        StaticEntityTableData table)
    {
        var definition = table.Definition;

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

            if (seen.Add(hash))
            {
                builder.Add(row);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<StaticEntityRow> OrderRows(
        StaticEntitySeedTableDefinition definition,
        ImmutableArray<StaticEntityRow> rows,
        OsmModel? model)
    {
        if (model is null || rows.IsDefaultOrEmpty || rows.Length <= 1)
        {
            return rows;
        }

        var constraints = GetSelfReferencingConstraints(definition, model);
        if (constraints.IsDefaultOrEmpty)
        {
            return rows;
        }

        return ApplySelfReferencingOrdering(definition, rows, constraints);
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

    private static ImmutableArray<SelfReferenceConstraint> GetSelfReferencingConstraints(
        StaticEntitySeedTableDefinition definition,
        OsmModel model)
    {
        if (model.Modules.IsDefaultOrEmpty)
        {
            return ImmutableArray<SelfReferenceConstraint>.Empty;
        }

        IEnumerable<ModuleModel> candidateModules = model.Modules;
        if (!string.IsNullOrWhiteSpace(definition.Module))
        {
            var moduleMatches = model.Modules
                .Where(module => string.Equals(module.Name.Value, definition.Module, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (moduleMatches.Length > 0)
            {
                candidateModules = moduleMatches;
            }
        }

        var entity = candidateModules
            .SelectMany(module => module.Entities)
            .FirstOrDefault(entity =>
                string.Equals(entity.Schema.Value, definition.Schema, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entity.PhysicalName.Value, definition.PhysicalName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
        {
            return ImmutableArray<SelfReferenceConstraint>.Empty;
        }

        if (entity.Relationships.IsDefaultOrEmpty)
        {
            return ImmutableArray<SelfReferenceConstraint>.Empty;
        }

        var columnLookup = CreateColumnLookup(definition.Columns);
        if (columnLookup.Count == 0)
        {
            return ImmutableArray<SelfReferenceConstraint>.Empty;
        }

        var constraints = ImmutableArray.CreateBuilder<SelfReferenceConstraint>();
        var schema = entity.Schema.Value;
        var table = entity.PhysicalName.Value;

        foreach (var relationship in entity.Relationships)
        {
            if (relationship.ActualConstraints.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var constraint in relationship.ActualConstraints)
            {
                var referencedSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                    ? schema
                    : constraint.ReferencedSchema;

                if (!string.Equals(referencedSchema, schema, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(constraint.ReferencedTable, table, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mappings = BuildColumnMappings(constraint, columnLookup);
                if (mappings.IsDefaultOrEmpty)
                {
                    continue;
                }

                constraints.Add(new SelfReferenceConstraint(mappings));
            }
        }

        return constraints.ToImmutable();
    }

    private static ImmutableArray<StaticEntityRow> ApplySelfReferencingOrdering(
        StaticEntitySeedTableDefinition definition,
        ImmutableArray<StaticEntityRow> rows,
        ImmutableArray<SelfReferenceConstraint> constraints)
    {
        if (rows.Length <= 1)
        {
            return rows;
        }

        var primaryIndices = GetPrimaryIndices(definition);
        if (primaryIndices.Length == 0)
        {
            return rows;
        }

        var applicable = constraints
            .Where(constraint => constraint.SupportsPrimaryKey(primaryIndices))
            .ToImmutableArray();

        if (applicable.IsDefaultOrEmpty)
        {
            return rows;
        }

        var rowByKey = new Dictionary<string, StaticEntityRow>(rows.Length, StringComparer.Ordinal);
        var orderLookup = new Dictionary<string, int>(rows.Length, StringComparer.Ordinal);

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var key = ComputeRowKey(row, primaryIndices);
            if (key is null || rowByKey.ContainsKey(key))
            {
                return rows;
            }

            rowByKey[key] = row;
            orderLookup[key] = index;
        }

        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var indegree = rowByKey.Keys.ToDictionary(static key => key, _ => 0, StringComparer.Ordinal);
        var hasDependency = false;

        foreach (var (key, row) in rowByKey)
        {
            foreach (var constraint in applicable)
            {
                var parentKey = TryComputeParentKey(row, primaryIndices, constraint);
                if (string.IsNullOrEmpty(parentKey))
                {
                    continue;
                }

                if (!rowByKey.ContainsKey(parentKey) || string.Equals(parentKey, key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!edges.TryGetValue(parentKey, out var dependents))
                {
                    dependents = new HashSet<string>(StringComparer.Ordinal);
                    edges[parentKey] = dependents;
                }

                if (dependents.Add(key))
                {
                    indegree[key] = indegree[key] + 1;
                    hasDependency = true;
                }
            }
        }

        if (!hasDependency)
        {
            return rows;
        }

        var ready = indegree
            .Where(static kvp => kvp.Value == 0)
            .Select(static kvp => kvp.Key)
            .OrderBy(key => orderLookup[key])
            .ToList();

        var ordered = ImmutableArray.CreateBuilder<StaticEntityRow>(rows.Length);
        var processed = new HashSet<string>(StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            var current = ready[0];
            ready.RemoveAt(0);
            processed.Add(current);
            ordered.Add(rowByKey[current]);

            if (!edges.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (!indegree.TryGetValue(neighbor, out var degree))
                {
                    continue;
                }

                var updated = degree - 1;
                indegree[neighbor] = updated;
                if (updated == 0)
                {
                    InsertByOriginalOrder(ready, neighbor, orderLookup);
                }
            }
        }

        if (processed.Count == rowByKey.Count)
        {
            return ordered.MoveToImmutable();
        }

        var remaining = rowByKey.Keys
            .Where(key => !processed.Contains(key))
            .OrderBy(key => orderLookup[key])
            .Select(key => rowByKey[key]);

        foreach (var row in remaining)
        {
            ordered.Add(row);
        }

        return ordered.MoveToImmutable();
    }

    private static string? TryComputeParentKey(
        StaticEntityRow row,
        IReadOnlyList<int> primaryIndices,
        SelfReferenceConstraint constraint)
    {
        if (!constraint.SupportsPrimaryKey(primaryIndices))
        {
            return null;
        }

        var maxIndex = GetMaximumIndex(primaryIndices);
        if (maxIndex < 0)
        {
            return null;
        }

        var buffer = new object?[maxIndex + 1];
        Array.Fill(buffer, _missingValue);

        foreach (var primaryIndex in primaryIndices)
        {
            if (!constraint.ParentToChild.TryGetValue(primaryIndex, out var childIndex))
            {
                return null;
            }

            var value = childIndex < row.Values.Length ? row.Values[childIndex] : null;
            if (value is null || value is DBNull)
            {
                return null;
            }

            buffer[primaryIndex] = value;
        }

        return ComputeRowKey(buffer, primaryIndices);
    }

    private static void InsertByOriginalOrder(
        IList<string> ready,
        string key,
        IReadOnlyDictionary<string, int> orderLookup)
    {
        var insertIndex = 0;
        for (; insertIndex < ready.Count; insertIndex++)
        {
            if (orderLookup[key] < orderLookup[ready[insertIndex]])
            {
                break;
            }
        }

        ready.Insert(insertIndex, key);
    }

    private static int GetMaximumIndex(IReadOnlyList<int> indices)
    {
        var max = -1;
        for (var i = 0; i < indices.Count; i++)
        {
            if (indices[i] > max)
            {
                max = indices[i];
            }
        }

        return max;
    }

    private static IReadOnlyDictionary<string, int> CreateColumnLookup(ImmutableArray<StaticEntitySeedColumn> columns)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            if (!string.IsNullOrWhiteSpace(column.ColumnName) && !lookup.ContainsKey(column.ColumnName))
            {
                lookup[column.ColumnName] = i;
            }

            if (!string.IsNullOrWhiteSpace(column.EffectiveColumnName) && !lookup.ContainsKey(column.EffectiveColumnName))
            {
                lookup[column.EffectiveColumnName] = i;
            }
        }

        return lookup;
    }

    private static ImmutableArray<ColumnMapping> BuildColumnMappings(
        RelationshipActualConstraint constraint,
        IReadOnlyDictionary<string, int> columnLookup)
    {
        if (constraint.Columns.IsDefaultOrEmpty)
        {
            return ImmutableArray<ColumnMapping>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ColumnMapping>();
        foreach (var column in constraint.Columns.OrderBy(static column => column.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(column.OwnerColumn) || string.IsNullOrWhiteSpace(column.ReferencedColumn))
            {
                continue;
            }

            if (!columnLookup.TryGetValue(column.OwnerColumn, out var childIndex))
            {
                continue;
            }

            if (!columnLookup.TryGetValue(column.ReferencedColumn, out var parentIndex))
            {
                continue;
            }

            builder.Add(new ColumnMapping(parentIndex, childIndex));
        }

        return builder.Count == 0 ? ImmutableArray<ColumnMapping>.Empty : builder.ToImmutable();
    }

    private static string? ComputeRowKey(StaticEntityRow row, IReadOnlyList<int> primaryIndices)
    {
        if (row.Values.IsDefault)
        {
            return null;
        }

        return ComputeRowKey(row.Values.AsSpan(), primaryIndices);
    }

    private static string? ComputeRowKey(ReadOnlySpan<object?> values, IReadOnlyList<int> primaryIndices)
    {
        if (primaryIndices.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var index in primaryIndices)
        {
            var value = index < values.Length ? values[index] : _missingValue;
            AppendKeyComponent(builder, value);
        }

        return builder.ToString();
    }

    private static string? ComputeRowKey(object?[] values, IReadOnlyList<int> primaryIndices)
    {
        if (values is null)
        {
            return null;
        }

        return ComputeRowKey(values.AsSpan(), primaryIndices);
    }

    private static void AppendKeyComponent(StringBuilder builder, object? value)
    {
        if (ReferenceEquals(value, _missingValue))
        {
            builder.Append('\u001f');
            return;
        }

        var text = value switch
        {
            null => "<null>",
            DateTime dt => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

        builder.Append(text);
        builder.Append('\u001f');
    }

    private sealed record ColumnMapping(int ParentIndex, int ChildIndex);

    private sealed class SelfReferenceConstraint
    {
        private readonly IReadOnlyDictionary<int, int> _parentToChild;

        public SelfReferenceConstraint(ImmutableArray<ColumnMapping> columns)
        {
            Columns = columns;

            var map = new Dictionary<int, int>();
            foreach (var column in columns)
            {
                if (!map.ContainsKey(column.ParentIndex))
                {
                    map[column.ParentIndex] = column.ChildIndex;
                }
            }

            _parentToChild = map;
        }

        public ImmutableArray<ColumnMapping> Columns { get; }

        public IReadOnlyDictionary<int, int> ParentToChild => _parentToChild;

        public bool SupportsPrimaryKey(IReadOnlyList<int> primaryIndices)
        {
            if (_parentToChild.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < primaryIndices.Count; i++)
            {
                if (!_parentToChild.ContainsKey(primaryIndices[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static ImmutableArray<DynamicEntityInsertScript> ApplyDependencyOrdering(
        ImmutableArray<DynamicEntityInsertScript> scripts,
        OsmModel? model)
    {
        if (scripts.IsDefaultOrEmpty || scripts.Length <= 1 || model is null)
        {
            return scripts;
        }

        var orderedDefinitions = EntityDependencySorter.SortByForeignKeys(
            scripts.Select(script => new StaticEntityTableData(script.Definition, ImmutableArray<StaticEntityRow>.Empty)).ToImmutableArray(),
            model);

        var definitions = orderedDefinitions.Tables;
        if (definitions.IsDefaultOrEmpty || definitions.Length != scripts.Length)
        {
            return scripts;
        }

        var lookup = new Dictionary<TableKey, DynamicEntityInsertScript>(TableKeyComparer.Instance);
        foreach (var script in scripts)
        {
            lookup[CreateTableKey(script.Definition)] = script;
        }

        var orderedScripts = ImmutableArray.CreateBuilder<DynamicEntityInsertScript>(scripts.Length);
        foreach (var definition in definitions)
        {
            var key = CreateTableKey(definition.Definition);
            if (!lookup.TryGetValue(key, out var script))
            {
                return scripts;
            }

            orderedScripts.Add(script);
        }

        return orderedScripts.MoveToImmutable();
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
            builder.AppendLine($"INSERT INTO {targetIdentifier} WITH (TABLOCK)");
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
            var span = rows.AsSpan(i, length);
            var builder = ImmutableArray.CreateBuilder<StaticEntityRow>(length);

            for (var j = 0; j < span.Length; j++)
            {
                builder.Add(span[j]);
            }

            yield return builder.MoveToImmutable();
        }
    }

}
