using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
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

    [Fact]
    public async Task ExecuteAsync_ShouldReturnTimeoutStatusesWhenSqlTimeoutOccurs()
    {
        var plan = new TableProfilingPlan(
            "dbo",
            "CUSTOMER",
            100,
            ImmutableArray.Create("ID", "TENANTID"),
            ImmutableArray.Create(new UniqueCandidatePlan("unique", ImmutableArray.Create("ID"))),
            ImmutableArray.Create(new ForeignKeyPlan("fk", "TENANTID", "dbo", "TENANT", "ID")));

        var executor = new ProfilingQueryExecutor(new ThrowingConnectionFactory(static () => new TimeoutDbConnection()), SqlProfilerOptions.Default);

        var results = await executor.ExecuteAsync(plan, CancellationToken.None);

        Assert.Equal(100, results.NullCounts["ID"]);
        Assert.Equal(ProfilingProbeOutcome.FallbackTimeout, results.NullCountStatuses["ID"].Outcome);
        Assert.True(results.UniqueDuplicates["unique"]);
        Assert.Equal(ProfilingProbeOutcome.FallbackTimeout, results.UniqueDuplicateStatuses["unique"].Outcome);
        Assert.True(results.ForeignKeys["fk"]);
        Assert.Equal(ProfilingProbeOutcome.FallbackTimeout, results.ForeignKeyStatuses["fk"].Outcome);
        Assert.True(results.ForeignKeyIsNoCheck["fk"]);
        Assert.Equal(ProfilingProbeOutcome.FallbackTimeout, results.ForeignKeyNoCheckStatuses["fk"].Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnCancelledStatusesWhenTableTimeoutTriggers()
    {
        var plan = new TableProfilingPlan(
            "dbo",
            "CUSTOMER",
            100,
            ImmutableArray.Create("ID"),
            ImmutableArray.Create(new UniqueCandidatePlan("unique", ImmutableArray.Create("ID"))),
            ImmutableArray<ForeignKeyPlan>.Empty);

        var limits = new SqlProfilerLimits(SqlProfilerOptions.Default.Limits.MaxRowsPerTable, TimeSpan.FromMilliseconds(1));
        var options = SqlProfilerOptions.Default with { Limits = limits };
        var executor = new ProfilingQueryExecutor(new ThrowingConnectionFactory(static () => new CancellableDbConnection()), options);

        var results = await executor.ExecuteAsync(plan, CancellationToken.None);

        Assert.Equal(100, results.NullCounts["ID"]);
        Assert.Equal(ProfilingProbeOutcome.Cancelled, results.NullCountStatuses["ID"].Outcome);
        Assert.True(results.UniqueDuplicates["unique"]);
        Assert.Equal(ProfilingProbeOutcome.Cancelled, results.UniqueDuplicateStatuses["unique"].Outcome);
    }

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        private readonly Func<DbConnection> _factory;

        public ThrowingConnectionFactory(Func<DbConnection> factory)
        {
            _factory = factory;
        }

        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_factory());
        }
    }

    private abstract class FakeDbConnectionBase : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;

        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotSupportedException();
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _state = ConnectionState.Closed;
        }
    }

    private sealed class TimeoutDbConnection : FakeDbConnectionBase
    {
        protected override DbCommand CreateDbCommand()
        {
            return new TimeoutDbCommand(this);
        }
    }

    private sealed class CancellableDbConnection : FakeDbConnectionBase
    {
        protected override DbCommand CreateDbCommand()
        {
            return new CancellableDbCommand(this);
        }
    }

    private sealed class TimeoutDbCommand : DbCommand
    {
        private readonly RecordingDbParameterCollection _parameters = new();
        private DbConnection? _connection;

        public TimeoutDbCommand(DbConnection connection)
        {
            _connection = connection;
        }

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.Both;

        protected override DbConnection DbConnection
        {
            get => _connection ?? throw new InvalidOperationException("Connection not set.");
            set => _connection = value;
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            throw new NotSupportedException();
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new RecordingDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new FakeTimeoutDbException();
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            return Task.FromException<DbDataReader>(new FakeTimeoutDbException());
        }
    }

    private sealed class CancellableDbCommand : DbCommand
    {
        private readonly RecordingDbParameterCollection _parameters = new();
        private readonly DbConnection _connection;

        public CancellableDbCommand(DbConnection connection)
        {
            _connection = connection;
        }

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.Both;

        protected override DbConnection DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            throw new NotSupportedException();
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new RecordingDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new OperationCanceledException();
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<DbDataReader>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken), useSynchronizationContext: false);
            return tcs.Task;
        }
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
