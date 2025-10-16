using System.Collections.Generic;
using System.Collections.Immutable;
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

        var expected =
            "WITH Source AS (\n" +
            "    SELECT TOP (@SampleSize) [ID], [EMAIL]\n" +
            "    FROM [dbo].[OSUSR_U_USER] WITH (NOLOCK)\n" +
            "    ORDER BY (SELECT NULL)\n" +
            ")\n" +
            "SELECT CandidateId, HasDuplicates\n" +
            "FROM (\n" +
            "    SELECT @candidate0 AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source GROUP BY [EMAIL] HAVING COUNT(*) > 1) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDuplicates\n" +
            ") AS results;\n";
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

        var expected =
            "WITH Source AS (\n" +
            "    SELECT [CUSTOMER_ID]\n" +
            "    FROM [dbo].[ORDERS] WITH (NOLOCK))\n" +
            "SELECT CandidateId, HasOrphans\n" +
            "FROM (\n" +
            "    SELECT @fk0 AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source AS source LEFT JOIN [dbo].[CUSTOMER] AS target WITH (NOLOCK) ON source.[CUSTOMER_ID] = target.[ID] WHERE source.[CUSTOMER_ID] IS NOT NULL AND target.[ID] IS NULL) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasOrphans\n" +
            ") AS results;\n";
        Assert.Equal(expected, sql);
        Assert.Single(command.ParametersCollection.Items, parameter => parameter.ParameterName == "@fk0");
    }
}
