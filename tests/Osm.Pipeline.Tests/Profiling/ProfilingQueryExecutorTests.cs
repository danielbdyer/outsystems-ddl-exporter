using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class ProfilingQueryExecutorTests
{
    [Fact]
    public void BuildUniqueCandidatesSql_ProjectsColumnsAndAddsParameters()
    {
        using var connection = RecordingDbConnection.WithResultSets();
        using var command = (RecordingDbCommand)connection.CreateCommand();

        var sql = ProfilingQueryExecutor.BuildUniqueCandidatesSql(
            "dbo",
            "OSUSR_U_USER",
            new[] { "ID", "EMAIL" },
            ImmutableArray.Create(new UniqueCandidatePlan("email", ImmutableArray.Create("EMAIL"))),
            useSampling: true,
            command);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "WITH Source AS (",
            "    SELECT TOP (@SampleSize) [ID], [EMAIL]",
            "    FROM [dbo].[OSUSR_U_USER] WITH (NOLOCK)",
            "    ORDER BY (SELECT NULL)",
            ")",
            "SELECT CandidateId, HasDuplicates",
            "FROM (",
            "    SELECT @candidate0 AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source GROUP BY [EMAIL] HAVING COUNT(*) > 1) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDuplicates",
            ") AS results;"
        }) + Environment.NewLine;
        Assert.Equal(expected, sql);
        Assert.Single(command.ParametersCollection.Items, parameter => parameter.ParameterName == "@candidate0");
    }

    [Fact]
    public void BuildForeignKeySql_ProjectsSourceColumns()
    {
        using var connection = RecordingDbConnection.WithResultSets();
        using var command = (RecordingDbCommand)connection.CreateCommand();

        var sql = ProfilingQueryExecutor.BuildForeignKeySql(
            "dbo",
            "ORDERS",
            new[] { "[CUSTOMER_ID]" },
            ImmutableArray.Create(new ForeignKeyPlan("fk", "CUSTOMER_ID", "dbo", "CUSTOMER", "ID")),
            useSampling: false,
            command);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "WITH Source AS (",
            "    SELECT [CUSTOMER_ID]",
            "    FROM [dbo].[ORDERS] WITH (NOLOCK))",
            "SELECT CandidateId, HasOrphans",
            "FROM (",
            "    SELECT @fk0 AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source AS source LEFT JOIN [dbo].[CUSTOMER] AS target WITH (NOLOCK) ON source.[CUSTOMER_ID] = target.[ID] WHERE source.[CUSTOMER_ID] IS NOT NULL AND target.[ID] IS NULL) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasOrphans",
            ") AS results;"
        }) + Environment.NewLine;
        Assert.Equal(expected, sql);
        Assert.Single(command.ParametersCollection.Items, parameter => parameter.ParameterName == "@fk0");
    }

    [Fact]
    public void BuildForeignKeyMetadataSql_ConstructsLookupWithParameters()
    {
        using var connection = RecordingDbConnection.WithResultSets();
        using var command = (RecordingDbCommand)connection.CreateCommand();

        var sql = ProfilingQueryExecutor.BuildForeignKeyMetadataSql("dbo", "ORDERS", command);

        var expected = """SELECT
    parentColumn.name AS ColumnName,
    targetSchema.name AS TargetSchema,
    targetTable.name AS TargetTable,
    targetColumn.name AS TargetColumn,
    fk.is_not_trusted AS IsNotTrusted,
    fk.is_disabled AS IsDisabled
FROM sys.foreign_keys AS fk
JOIN sys.tables AS parentTable ON fk.parent_object_id = parentTable.object_id
JOIN sys.schemas AS parentSchema ON parentTable.schema_id = parentSchema.schema_id
JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns AS parentColumn ON fkc.parent_object_id = parentColumn.object_id AND fkc.parent_column_id = parentColumn.column_id
JOIN sys.tables AS targetTable ON fk.referenced_object_id = targetTable.object_id
JOIN sys.schemas AS targetSchema ON targetTable.schema_id = targetSchema.schema_id
JOIN sys.columns AS targetColumn ON fkc.referenced_object_id = targetColumn.object_id AND fkc.referenced_column_id = targetColumn.column_id
WHERE parentSchema.name = @SchemaName AND parentTable.name = @TableName;""";

        Assert.Equal(expected, sql);
        Assert.Collection(
            command.ParametersCollection.Items,
            parameter =>
            {
                Assert.Equal("@SchemaName", parameter.ParameterName);
                Assert.Equal(DbType.String, parameter.DbType);
                Assert.Equal("dbo", parameter.Value);
            },
            parameter =>
            {
                Assert.Equal("@TableName", parameter.ParameterName);
                Assert.Equal(DbType.String, parameter.DbType);
                Assert.Equal("ORDERS", parameter.Value);
            });
    }
}
