using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class TableMetadataLoaderTests
{
    [Fact]
    public async Task LoadColumnMetadataAsync_BuildsExpectedSql()
    {
        var options = SqlProfilerOptions.Default with { CommandTimeoutSeconds = 42 };
        var loader = new TableMetadataLoader(options);
        using var connection = RecordingDbConnection.WithResultSets(new FakeCommandDefinition(new[]
        {
            new object?[] { "dbo", "OSUSR_U_USER", "EMAIL", true, false, 0, null }
        }));

        var tables = new List<TableCoordinate>
        {
            TableCoordinate.Create("dbo", "OSUSR_U_USER").Value,
            TableCoordinate.Create("sales", "ORDERS").Value
        };

        var metadata = await loader.LoadColumnMetadataAsync(connection, tables, CancellationToken.None);

        var expectedSql = """
        SELECT
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
        WHERE t.is_ms_shipped = 0 AND EXISTS (SELECT 1 FROM (VALUES (@schema0, @table0), (@schema1, @table1)) AS targets(SchemaName, TableName) WHERE targets.SchemaName = s.name AND targets.TableName = t.name);
        """;

        Assert.Equal(expectedSql, connection.LastCommand!.CommandText);
        Assert.Equal(4, connection.LastCommand.ParametersCollection.Count);
        Assert.Equal(42, connection.LastCommand.CommandTimeout);

        var key = ("dbo", "OSUSR_U_USER", "EMAIL");
        Assert.True(metadata.TryGetValue(key, out var column));
        Assert.True(column.IsNullable);
        Assert.False(column.IsPrimaryKey);
    }

    [Fact]
    public async Task LoadRowCountsAsync_BuildsExpectedSql()
    {
        var options = SqlProfilerOptions.Default;
        var loader = new TableMetadataLoader(options);
        using var connection = RecordingDbConnection.WithResultSets(new FakeCommandDefinition(new[]
        {
            new object?[] { "dbo", "OSUSR_U_USER", 100L }
        }));

        var tables = new List<TableCoordinate>
        {
            TableCoordinate.Create("dbo", "OSUSR_U_USER").Value
        };

        var counts = await loader.LoadRowCountsAsync(connection, tables, CancellationToken.None);

        var expectedSql = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            SUM(p.rows) AS [RowCount]
        FROM sys.tables AS t
        JOIN sys.schemas AS s ON t.schema_id = s.schema_id
        JOIN sys.dm_db_partition_stats AS p ON t.object_id = p.object_id
        WHERE p.index_id IN (0,1) AND EXISTS (SELECT 1 FROM (VALUES (@schema0, @table0)) AS targets(SchemaName, TableName) WHERE targets.SchemaName = s.name AND targets.TableName = t.name)
        GROUP BY s.name, t.name;
        """;

        Assert.Equal(expectedSql, connection.LastCommand!.CommandText);
        Assert.Single(connection.LastCommand.ParametersCollection.Items.Where(parameter => parameter.ParameterName.StartsWith("@schema", System.StringComparison.Ordinal)));
        Assert.Equal(100L, counts[TableCoordinate.Create("dbo", "OSUSR_U_USER").Value]);
    }
}
