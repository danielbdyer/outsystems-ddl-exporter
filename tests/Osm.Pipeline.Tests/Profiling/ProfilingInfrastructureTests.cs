using System;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class ProfilingInfrastructureTests
{
    [Fact]
    public void NullCountQueryBuilder_ConfiguresSamplingCommand()
    {
        using var connection = RecordingDbConnection.WithResultSets();
        using var command = (RecordingDbCommand)connection.CreateCommand();

        var plan = new TableProfilingPlan(
            "dbo",
            "OSUSR_U_USER",
            100,
            ImmutableArray.Create("ID", "EMAIL"),
            ImmutableArray<UniqueCandidatePlan>.Empty,
            ImmutableArray<ForeignKeyPlan>.Empty);

        var builder = new NullCountQueryBuilder();
        builder.Configure(command, plan, useSampling: true, sampleSize: 25);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "WITH Source AS (",
            "    SELECT TOP (@SampleSize) [ID], [EMAIL]",
            "    FROM [dbo].[OSUSR_U_USER] WITH (NOLOCK)",
            "    ORDER BY (SELECT NULL)",
            ")",
            "SELECT ColumnName, NullCount",
            "FROM (",
            "    SELECT 'ID' AS ColumnName, SUM(CASE WHEN [ID] IS NULL THEN 1 ELSE 0 END) AS NullCount",
            "    FROM Source",
            "    UNION ALL",
            "    SELECT 'EMAIL' AS ColumnName, SUM(CASE WHEN [EMAIL] IS NULL THEN 1 ELSE 0 END) AS NullCount",
            "    FROM Source",
            ") AS results;"
        }) + Environment.NewLine;

        Assert.Equal(expected, command.CommandText);
        Assert.Collection(
            command.ParametersCollection.Items,
            parameter =>
            {
                Assert.Equal("@SampleSize", parameter.ParameterName);
                Assert.Equal(DbType.Int32, parameter.DbType);
                Assert.Equal(25, parameter.Value);
            });
    }

    [Fact]
    public void UniqueCandidateQueryBuilder_ConfiguresCommand()
    {
        using var connection = RecordingDbConnection.WithResultSets();
        using var command = (RecordingDbCommand)connection.CreateCommand();

        var plan = new TableProfilingPlan(
            "dbo",
            "OSUSR_U_USER",
            100,
            ImmutableArray.Create("ID", "EMAIL"),
            ImmutableArray.Create(new UniqueCandidatePlan("email", ImmutableArray.Create("ID", "EMAIL"))),
            ImmutableArray<ForeignKeyPlan>.Empty);

        var builder = new UniqueCandidateQueryBuilder();
        builder.Configure(command, plan, useSampling: true, sampleSize: 10);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "WITH Source AS (",
            "    SELECT TOP (@SampleSize) [ID], [EMAIL]",
            "    FROM [dbo].[OSUSR_U_USER] WITH (NOLOCK)",
            "    ORDER BY (SELECT NULL)",
            ")",
            "SELECT CandidateId, HasDuplicates",
            "FROM (",
            "    SELECT @candidate0 AS CandidateId, CASE WHEN EXISTS (SELECT 1 FROM Source GROUP BY [ID], [EMAIL] HAVING COUNT(*) > 1) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDuplicates",
            ") AS results;"
        }) + Environment.NewLine;

        Assert.Equal(expected, command.CommandText);
        Assert.Collection(
            command.ParametersCollection.Items,
            parameter =>
            {
                Assert.Equal("@candidate0", parameter.ParameterName);
                Assert.Equal(DbType.String, parameter.DbType);
                Assert.Equal("email", parameter.Value);
            },
            parameter =>
            {
                Assert.Equal("@SampleSize", parameter.ParameterName);
                Assert.Equal(DbType.Int32, parameter.DbType);
                Assert.Equal(10, parameter.Value);
            });
    }

    [Fact]
    public void ForeignKeyProbeQueryBuilder_ConfiguresRealityCommand()
    {
        using var connection = RecordingDbConnection.WithResultSets();
        using var command = (RecordingDbCommand)connection.CreateCommand();

        var plan = new TableProfilingPlan(
            "dbo",
            "ORDERS",
            100,
            ImmutableArray.Create("CUSTOMER_ID"),
            ImmutableArray<UniqueCandidatePlan>.Empty,
            ImmutableArray.Create(new ForeignKeyPlan("fk", "CUSTOMER_ID", "dbo", "CUSTOMER", "ID")));

        var builder = new ForeignKeyProbeQueryBuilder();
        builder.ConfigureRealityCommand(command, plan, useSampling: false, sampleSize: 0);

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

        Assert.Equal(expected, command.CommandText);
        Assert.Single(command.ParametersCollection.Items, parameter => parameter.ParameterName == "@fk0");
    }

    [Fact]
    public void ForeignKeyProbeQueryBuilder_ConfiguresMetadataCommand()
    {
        using var connection = RecordingDbConnection.WithResultSets();
        using var command = (RecordingDbCommand)connection.CreateCommand();

        var builder = new ForeignKeyProbeQueryBuilder();
        builder.ConfigureMetadataCommand(command, "dbo", "ORDERS");

        var expected = """
SELECT
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
WHERE parentSchema.name = @SchemaName AND parentTable.name = @TableName;
""";

        Assert.Equal(expected, command.CommandText);
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

    [Fact]
    public void TableSamplingPolicy_ShouldRespectThreshold()
    {
        var options = SqlProfilerOptions.Default with
        {
            Sampling = new SqlSamplingOptions(10, 5),
            Limits = new SqlProfilerLimits(maxRowsPerTable: null, tableTimeout: null)
        };

        Assert.False(TableSamplingPolicy.ShouldSample(10, options));
        Assert.True(TableSamplingPolicy.ShouldSample(11, options));
    }

    [Fact]
    public void TableSamplingPolicy_GetSampleSizeHonorsRowCountAndLimits()
    {
        var options = SqlProfilerOptions.Default with
        {
            Sampling = new SqlSamplingOptions(100, 50),
            Limits = new SqlProfilerLimits(maxRowsPerTable: 25, tableTimeout: null)
        };

        var sampleSize = TableSamplingPolicy.GetSampleSize(40, options);
        Assert.Equal(25, sampleSize);
    }

    [Fact]
    public void TableSamplingPolicy_DetermineSampleSizeFallsBackToRowCountWhenNotSampling()
    {
        var options = SqlProfilerOptions.Default with
        {
            Sampling = new SqlSamplingOptions(1000, 100),
            Limits = new SqlProfilerLimits(maxRowsPerTable: null, tableTimeout: null)
        };

        var size = TableSamplingPolicy.DetermineSampleSize(25, options);
        Assert.Equal(25, size);
    }

    [Fact]
    public async Task ProfilingProbePolicy_ReturnsFallbackOnTimeoutAsync()
    {
        var policy = new ProfilingProbePolicy(new StaticTimeProvider());
        var fallback = new object();

        var result = await policy.ExecuteAsync(
            _ => Task.FromException<object>(new FakeTimeoutDbException()),
            fallback,
            sampleSize: 42,
            tableCancellation: null,
            originalToken: CancellationToken.None);

        Assert.Same(fallback, result.Value);
        Assert.Equal(ProfilingProbeOutcome.FallbackTimeout, result.Status.Outcome);
        Assert.Equal(42, result.Status.SampleSize);
    }

    [Fact]
    public async Task ProfilingProbePolicy_ReturnsCancelledStatusWhenTableTimeoutTriggersAsync()
    {
        var policy = new ProfilingProbePolicy(new StaticTimeProvider());
        var fallback = new object();
        using var tableCancellation = new CancellationTokenSource();
        tableCancellation.Cancel();

        var result = await policy.ExecuteAsync(
            _ => Task.FromCanceled<object>(tableCancellation.Token),
            fallback,
            sampleSize: 10,
            tableCancellation,
            CancellationToken.None);

        Assert.Same(fallback, result.Value);
        Assert.Equal(ProfilingProbeOutcome.Cancelled, result.Status.Outcome);
        Assert.Equal(10, result.Status.SampleSize);
    }

    [Fact]
    public async Task ProfilingQueryExecutor_RecordsTelemetryForSampledTable()
    {
        var uniqueKey = ProfilingPlanBuilder.BuildUniqueKey(new[] { "ID" });
        var foreignKeyKey = ProfilingPlanBuilder.BuildForeignKeyKey("CUSTOMER_ID", "dbo", "CUSTOMER", "ID");

        var connection = RecordingDbConnection.WithResultSets(
            new FakeCommandDefinition(new[]
            {
                new object?[] { "ID", 0L },
                new object?[] { "CUSTOMER_ID", 1L }
            }),
            new FakeCommandDefinition(new[]
            {
                new object?[] { uniqueKey, false }
            }),
            new FakeCommandDefinition(new[]
            {
                new object?[] { foreignKeyKey, false }
            }),
            new FakeCommandDefinition(new[]
            {
                new object?[] { "CUSTOMER_ID", "dbo", "CUSTOMER", "ID", false, false }
            }));

        var options = new SqlProfilerOptions(
            commandTimeoutSeconds: null,
            sampling: SqlSamplingOptions.Create(10, 4),
            maxConcurrentTableProfiles: 1,
            limits: new SqlProfilerLimits(null, null),
            namingOverrides: NamingOverrideOptions.Empty);

        var collector = new ProfilingTelemetryCollector();
        var executor = new ProfilingQueryExecutor(
            new StubConnectionFactory(connection),
            options,
            metadataLog: null,
            telemetryCollector: collector);

        var plan = new TableProfilingPlan(
            "dbo",
            "ORDERS",
            100,
            ImmutableArray.Create("ID", "CUSTOMER_ID"),
            ImmutableArray.Create(new UniqueCandidatePlan(uniqueKey, ImmutableArray.Create("ID"))),
            ImmutableArray.Create(new ForeignKeyPlan(foreignKeyKey, "CUSTOMER_ID", "dbo", "CUSTOMER", "ID")));

        await executor.ExecuteAsync(plan, CancellationToken.None);

        var telemetry = collector.ToImmutableArray();
        Assert.Single(telemetry);
        var entry = telemetry[0];

        Assert.True(entry.Sampled);
        Assert.Equal(4, entry.SampleSize);
        Assert.Equal(plan.RowCount, entry.RowCount);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, entry.NullCountStatus.Outcome);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, entry.UniqueCandidateStatus.Outcome);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, entry.ForeignKeyStatus.Outcome);
    }

    [Fact]
    public async Task ProfilingQueryExecutor_RecordsTelemetryForFullScanTable()
    {
        var uniqueKey = ProfilingPlanBuilder.BuildUniqueKey(new[] { "ID" });
        var foreignKeyKey = ProfilingPlanBuilder.BuildForeignKeyKey("CUSTOMER_ID", "dbo", "CUSTOMER", "ID");

        var connection = RecordingDbConnection.WithResultSets(
            new FakeCommandDefinition(new[]
            {
                new object?[] { "ID", 0L },
                new object?[] { "CUSTOMER_ID", 0L }
            }),
            new FakeCommandDefinition(new[]
            {
                new object?[] { uniqueKey, false }
            }),
            new FakeCommandDefinition(new[]
            {
                new object?[] { foreignKeyKey, false }
            }),
            new FakeCommandDefinition(new[]
            {
                new object?[] { "CUSTOMER_ID", "dbo", "CUSTOMER", "ID", false, false }
            }));

        var options = new SqlProfilerOptions(
            commandTimeoutSeconds: null,
            sampling: SqlSamplingOptions.Create(10, 4),
            maxConcurrentTableProfiles: 1,
            limits: new SqlProfilerLimits(null, null),
            namingOverrides: NamingOverrideOptions.Empty);

        var collector = new ProfilingTelemetryCollector();
        var executor = new ProfilingQueryExecutor(
            new StubConnectionFactory(connection),
            options,
            metadataLog: null,
            telemetryCollector: collector);

        var plan = new TableProfilingPlan(
            "dbo",
            "ORDERS",
            5,
            ImmutableArray.Create("ID", "CUSTOMER_ID"),
            ImmutableArray.Create(new UniqueCandidatePlan(uniqueKey, ImmutableArray.Create("ID"))),
            ImmutableArray.Create(new ForeignKeyPlan(foreignKeyKey, "CUSTOMER_ID", "dbo", "CUSTOMER", "ID")));

        await executor.ExecuteAsync(plan, CancellationToken.None);

        var telemetry = collector.ToImmutableArray();
        Assert.Single(telemetry);
        var entry = telemetry[0];

        Assert.False(entry.Sampled);
        Assert.Equal(plan.RowCount, entry.SampleSize);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, entry.NullCountStatus.Outcome);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, entry.UniqueCandidateStatus.Outcome);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, entry.ForeignKeyStatus.Outcome);
    }

    private sealed class StubConnectionFactory : IDbConnectionFactory
    {
        private readonly DbConnection _connection;

        public StubConnectionFactory(DbConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_connection);
        }
    }

    private sealed class StaticTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public StaticTimeProvider()
        {
            _now = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeTimeoutDbException : DbException
    {
        public FakeTimeoutDbException()
            : base("Timeout occurred.")
        {
        }

        public override int ErrorCode => -2;
    }
}
