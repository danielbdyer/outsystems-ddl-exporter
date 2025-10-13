using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.RemapUsers;

public sealed class SqlSchemaGraph : ISchemaGraph
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlSchemaGraph(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<SchemaTable>> GetTablesAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name;";

        await using var connection = (SqlConnection)await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var tables = new List<SchemaTable>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(new SchemaTable(reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }

    public async Task<IReadOnlyList<SchemaForeignKey>> GetForeignKeysAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    fk.name,
    SCHEMA_NAME(pt.schema_id) AS SourceSchema,
    pt.name AS SourceTable,
    pc.name AS SourceColumn,
    SCHEMA_NAME(rt.schema_id) AS TargetSchema,
    rt.name AS TargetTable,
    rc.name AS TargetColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc
    ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables pt
    ON pt.object_id = fk.parent_object_id
JOIN sys.columns pc
    ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.tables rt
    ON rt.object_id = fk.referenced_object_id
JOIN sys.columns rc
    ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
WHERE fk.is_ms_shipped = 0
ORDER BY SourceSchema, SourceTable, fk.name, fkc.constraint_column_id;";

        await using var connection = (SqlConnection)await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var fks = new List<SchemaForeignKey>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var sourceSchema = reader.GetString(1);
            var sourceTable = reader.GetString(2);
            var sourceColumn = reader.GetString(3);
            var targetSchema = reader.GetString(4);
            var targetTable = reader.GetString(5);
            var targetColumn = reader.GetString(6);

            fks.Add(new SchemaForeignKey(
                name,
                new SchemaTable(sourceSchema, sourceTable),
                sourceColumn,
                new SchemaTable(targetSchema, targetTable),
                targetColumn));
        }

        return fks;
    }

    public async Task<IReadOnlyList<SchemaTable>> GetTopologicallySortedTablesAsync(CancellationToken cancellationToken)
    {
        var tables = await GetTablesAsync(cancellationToken).ConfigureAwait(false);
        var foreignKeys = await GetForeignKeysAsync(cancellationToken).ConfigureAwait(false);

        var comparer = new SchemaTableComparer();
        var tableLookup = tables.ToDictionary(table => (table.Schema, table.Name), table => table, comparer);
        var adjacency = tables.ToDictionary(table => table, _ => new HashSet<SchemaTable>(comparer), comparer);
        var incomingCounts = tables.ToDictionary(table => table, _ => 0, comparer);

        foreach (var fk in foreignKeys)
        {
            if (!tableLookup.TryGetValue((fk.SourceTable.Schema, fk.SourceTable.Name), out var source))
            {
                continue;
            }

            if (!tableLookup.TryGetValue((fk.TargetTable.Schema, fk.TargetTable.Name), out var target))
            {
                continue;
            }

            if (adjacency[source].Add(target))
            {
                incomingCounts[target]++;
            }
        }

        var result = new List<SchemaTable>();
        var queue = new SortedSet<SchemaTable>(new SchemaTableSortComparer());
        foreach (var pair in incomingCounts)
        {
            if (pair.Value == 0)
            {
                queue.Add(pair.Key);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Min!;
            queue.Remove(current);
            result.Add(current);

            foreach (var target in adjacency[current])
            {
                incomingCounts[target]--;
                if (incomingCounts[target] == 0)
                {
                    queue.Add(target);
                }
            }
        }

        if (result.Count < tables.Count)
        {
            var remaining = tables.Except(result, comparer)
                .OrderBy(table => table.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(table => table.Name, StringComparer.OrdinalIgnoreCase);
            result.AddRange(remaining);
        }

        return result;
    }

    private sealed class SchemaTableComparer : IEqualityComparer<(string Schema, string Name)>, IEqualityComparer<SchemaTable>
    {
        public bool Equals((string Schema, string Name) x, (string Schema, string Name) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Schema, string Name) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
        }

        public bool Equals(SchemaTable? x, SchemaTable? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return Equals((x.Schema, x.Name), (y.Schema, y.Name));
        }

        public int GetHashCode(SchemaTable obj)
        {
            return GetHashCode((obj.Schema, obj.Name));
        }
    }

    private sealed class SchemaTableSortComparer : IComparer<SchemaTable>
    {
        public int Compare(SchemaTable? x, SchemaTable? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var schemaComparison = string.Compare(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase);
            if (schemaComparison != 0)
            {
                return schemaComparison;
            }

            return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
