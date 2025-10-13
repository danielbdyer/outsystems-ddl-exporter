using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
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

    [Fact]
    public void DryRunHash_IsDeterministic_ForIdenticalInputs()
    {
        using var temp = new TempDirectory();
        var snapshotFile = Path.Combine(temp.Path, "dbo.ossys_User.json");
        File.WriteAllText(snapshotFile, "[]");

        var context1 = CreateContext(includePii: false, snapshotPath: temp.Path);
        var context2 = CreateContext(includePii: false, snapshotPath: temp.Path);

        context1.DryRunHash.Should().Be(context2.DryRunHash);
    }

    private static RemapUsersContext CreateContext(bool includePii, string? snapshotPath = null)
    {
        var schemaGraph = new StubSchemaGraph();
        var sqlRunner = new StubSqlRunner();
        var bulkLoader = new StubBulkLoader();
        var telemetry = new StubTelemetry();
        var artifacts = new StubArtifactWriter();

        snapshotPath ??= "/snapshots/dev";

        return new RemapUsersContext(
            sourceEnvironment: "DEV",
            uatConnectionString: "Server=(local);Database=UAT;Trusted_Connection=True;",
            snapshotPath: snapshotPath,
            matchingRules: new[] { "email" },
            fallbackUserId: null,
            policy: RemapUsersPolicy.Reassign,
            policyWasExplicit: true,
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
            rebuildMap: false);
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
