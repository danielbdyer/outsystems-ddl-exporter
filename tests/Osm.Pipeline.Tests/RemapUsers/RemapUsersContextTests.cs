using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Osm.Pipeline.RemapUsers;
using Xunit;

namespace Osm.Pipeline.Tests.RemapUsers;

public sealed class RemapUsersContextTests
{
    [Fact]
    public void RedactIdentifier_ReturnsHash_WhenPiiExcluded()
    {
        var context = CreateContext(includePii: false);
        var result = context.RedactIdentifier("user@example.com");
        result.Should().StartWith("hash:");
        result.Should().NotContain("user@example.com");
    }

    [Fact]
    public void RedactIdentifier_ReturnsOriginal_WhenPiiIncluded()
    {
        var context = CreateContext(includePii: true);
        var value = "user@example.com";
        context.RedactIdentifier(value).Should().Be(value);
    }

    private static RemapUsersContext CreateContext(bool includePii)
    {
        var schemaGraph = new StubSchemaGraph();
        var sqlRunner = new StubSqlRunner();
        var bulkLoader = new StubBulkLoader();
        var telemetry = new StubTelemetry();
        var artifacts = new StubArtifactWriter();
        var runParameters = new RemapUsersRunParameters(
            "DEV",
            "/snapshots/dev",
            new[] { "email" },
            RemapUsersPolicy.Reassign,
            includePii,
            RebuildMap: false,
            DryRun: true,
            UserTable: "dbo.ossys_User",
            BatchSize: 100,
            CommandTimeoutSeconds: 60,
            Parallelism: 1,
            FallbackUserId: null).Normalize();

        return new RemapUsersContext(
            sourceEnvironment: "DEV",
            uatConnectionString: "Server=(local);Database=UAT;Trusted_Connection=True;",
            snapshotPath: "/snapshots/dev",
            matchingRules: new[] { "email" },
            fallbackUserId: null,
            policy: RemapUsersPolicy.Reassign,
            dryRun: true,
            artifactDirectory: "./artifacts",
            batchSize: 100,
            commandTimeoutSeconds: 60,
            parallelism: 1,
            userTable: "dbo.ossys_User",
            schemaGraph,
            sqlRunner,
            bulkLoader,
            telemetry,
            artifacts,
            logLevel: RemapUsersLogLevel.Info,
            includePii,
            rebuildMap: false,
            runParameters,
            policyExplicit: true);
    }

    private sealed class StubSchemaGraph : ISchemaGraph
    {
        public Task<IReadOnlyList<SchemaTable>> GetTablesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SchemaTable>>(new List<SchemaTable>());

        public Task<IReadOnlyList<SchemaForeignKey>> GetForeignKeysAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SchemaForeignKey>>(new List<SchemaForeignKey>());

        public Task<IReadOnlyList<SchemaTable>> GetTopologicallySortedTablesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SchemaTable>>(new List<SchemaTable>());
    }

    private sealed class StubSqlRunner : ISqlRunner
    {
        public Task<int> ExecuteAsync(string commandText, IReadOnlyDictionary<string, object?> parameters, System.TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<TResult?> ExecuteScalarAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, System.TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult<TResult?>(default);

        public Task<IReadOnlyList<TResult>> QueryAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, System.Func<IDataRecord, TResult> projector, System.TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TResult>>(new List<TResult>());

        public Task ExecuteInTransactionAsync(string transactionName, System.TimeSpan timeout, System.Func<ISqlTransactionalRunner, CancellationToken, Task> work, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubBulkLoader : IBulkLoader
    {
        public Task LoadAsync(BulkLoadRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubTelemetry : IRemapUsersTelemetry
    {
        public IReadOnlyList<RemapUsersTelemetryEntry> Entries { get; } = new List<RemapUsersTelemetryEntry>();

        public void Error(string stepName, string message, System.Exception exception, IReadOnlyDictionary<string, string?>? metadata = null) { }

        public void Info(string stepName, string message, IReadOnlyDictionary<string, string?>? metadata = null) { }

        public void StepCompleted(string stepName) { }

        public void StepStarted(string stepName) { }

        public void Warning(string stepName, string message, IReadOnlyDictionary<string, string?>? metadata = null) { }
    }

    private sealed class StubArtifactWriter : IRemapUsersArtifactWriter
    {
        public Task WriteCsvAsync(string relativePath, IEnumerable<IReadOnlyList<string>> rows, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteJsonAsync(string relativePath, object payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
