using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

internal sealed class TableMetadataLoader : ITableMetadataLoader
{
    private readonly SqlProfilerOptions _options;
    private readonly SqlMetadataLog? _metadataLog;

    public TableMetadataLoader(SqlProfilerOptions options, SqlMetadataLog? metadataLog = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metadataLog = metadataLog;
    }

    public async Task<Dictionary<(string Schema, string Table, string Column), ColumnMetadata>> LoadColumnMetadataAsync(
        DbConnection connection,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance);
        if (tables.Count == 0)
        {
            return metadata;
        }

        await using var command = connection.CreateCommand();
        var filterClause = BuildTableFilterClause(command, tables, "s.name", "t.name");
        command.CommandText = @$"SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    c.name AS ColumnName,
    c.is_nullable,
    c.is_computed,
    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
    dc.definition AS DefaultDefinition
FROM sys.columns AS c
JOIN sys.tables AS t ON c.object_id = t.object_id
JOIN sys.schemas AS s ON t.schema_id = s.schema_id
LEFT JOIN sys.default_constraints AS dc ON c.default_object_id = dc.object_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) AS pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
WHERE t.is_ms_shipped = 0 AND {filterClause};";

        ApplyCommandTimeout(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            if (!tables.Contains((schema, table)))
            {
                continue;
            }

            var column = reader.GetString(2);
            var isNullable = reader.GetBoolean(3);
            var isComputed = reader.GetBoolean(4);
            var isPrimaryKey = reader.GetInt32(5) == 1;
            var defaultDefinition = reader.IsDBNull(6) ? null : reader.GetString(6);

            metadata[(schema, table, column)] = new ColumnMetadata(isNullable, isComputed, isPrimaryKey, defaultDefinition);
        }

        _metadataLog?.RecordRequest(
            "sql.tableMetadata",
            metadata
                .OrderBy(static entry => entry.Key.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Key.Table, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Key.Column, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new
                {
                    schema = entry.Key.Schema,
                    table = entry.Key.Table,
                    column = entry.Key.Column,
                    entry.Value.IsNullable,
                    entry.Value.IsComputed,
                    entry.Value.IsPrimaryKey,
                    entry.Value.DefaultDefinition
                })
                .ToArray());

        return metadata;
    }

    public async Task<Dictionary<(string Schema, string Table), long>> LoadRowCountsAsync(
        DbConnection connection,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var counts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance);
        if (tables.Count == 0)
        {
            return counts;
        }

        await using var command = connection.CreateCommand();
        var filterClause = BuildTableFilterClause(command, tables, "s.name", "t.name");
        command.CommandText = @$"SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    SUM(p.row_count) AS [RowCount]
FROM sys.tables AS t
JOIN sys.schemas AS s ON t.schema_id = s.schema_id
JOIN sys.dm_db_partition_stats AS p ON t.object_id = p.object_id
WHERE p.index_id IN (0,1) AND {filterClause}
GROUP BY s.name, t.name;";

        ApplyCommandTimeout(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            if (!tables.Contains((schema, table)))
            {
                continue;
            }

            var count = reader.GetInt64(2);
            counts[(schema, table)] = count;
        }

        _metadataLog?.RecordRequest(
            "sql.tableRowCounts",
            counts
                .OrderBy(static entry => entry.Key.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Key.Table, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new
                {
                    schema = entry.Key.Schema,
                    table = entry.Key.Table,
                    rowCount = entry.Value
                })
                .ToArray());

        return counts;
    }

    internal static string BuildTableFilterClause(
        DbCommand command,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        string schemaColumn,
        string tableColumn)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (tables.Count == 0)
        {
            return "1 = 0";
        }

        var builder = new System.Text.StringBuilder();
        builder.Append("EXISTS (SELECT 1 FROM (VALUES ");

        var index = 0;
        foreach (var (schema, table) in tables)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            var schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = $"@schema{index}";
            schemaParameter.DbType = DbType.String;
            schemaParameter.Value = schema;
            command.Parameters.Add(schemaParameter);

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = $"@table{index}";
            tableParameter.DbType = DbType.String;
            tableParameter.Value = table;
            command.Parameters.Add(tableParameter);

            builder.Append($"({schemaParameter.ParameterName}, {tableParameter.ParameterName})");
            index++;
        }

        builder.Append($") AS targets(SchemaName, TableName) WHERE targets.SchemaName = {schemaColumn} AND targets.TableName = {tableColumn})");
        return builder.ToString();
    }

    private void ApplyCommandTimeout(DbCommand command)
    {
        if (_options.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }
    }
}
