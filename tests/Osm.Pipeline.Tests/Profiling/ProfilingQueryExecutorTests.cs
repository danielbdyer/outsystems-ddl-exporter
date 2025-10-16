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

        var expected = @"WITH Source AS (
    SELECT TOP (@SampleSize) [ID], [EMAIL]
    FROM [dbo].[OSUSR_U_USER] WITH (NOLOCK)
    ORDER BY (SELECT NULL)
)
SELECT CandidateId, HasDuplicates
FROM (
    SELECT @candidate0 AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source GROUP BY [EMAIL] HAVING COUNT(*) > 1) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDuplicates
) AS results;
";
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

        var expected = @"WITH Source AS (
    SELECT [CUSTOMER_ID]
    FROM [dbo].[ORDERS] WITH (NOLOCK))
SELECT CandidateId, HasOrphans
FROM (
    SELECT @fk0 AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source AS source LEFT JOIN [dbo].[CUSTOMER] AS target WITH (NOLOCK) ON source.[CUSTOMER_ID] = target.[ID] WHERE source.[CUSTOMER_ID] IS NOT NULL AND target.[ID] IS NULL) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasOrphans
) AS results;
";
        Assert.Equal(expected, sql);
        Assert.Single(command.ParametersCollection.Items, parameter => parameter.ParameterName == "@fk0");
    }
}
