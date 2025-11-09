using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using FluentAssertions;
using Osm.LoadHarness;
using Osm.TestSupport;
using Tests.Support;
using Xunit;

namespace Osm.LoadHarness.Integration.Tests;

public sealed class LoadHarnessRunnerIntegrationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;
    private readonly IFileSystem _fileSystem = new FileSystem();

    public LoadHarnessRunnerIntegrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    [DockerFact]
    public async Task RunAsync_ReplaysScriptsAndWritesReport()
    {
        using var temp = new TempDirectory();
        var safeScriptPath = _fileSystem.Path.Combine(temp.Path, "safe.sql");
        var staticSeedPath = _fileSystem.Path.Combine(temp.Path, "static-seed.sql");
        var reportPath = _fileSystem.Path.Combine(temp.Path, "harness-report.json");

        await _fileSystem.File.WriteAllTextAsync(safeScriptPath, @"IF OBJECT_ID('dbo.LoadHarness', 'U') IS NOT NULL
    DROP TABLE dbo.LoadHarness;
GO
CREATE TABLE dbo.LoadHarness
(
    Id INT IDENTITY(1, 1) PRIMARY KEY,
    Payload NVARCHAR(50) NOT NULL
);
GO
CREATE NONCLUSTERED INDEX IX_LoadHarness_Payload ON dbo.LoadHarness(Payload);
GO");

        await _fileSystem.File.WriteAllTextAsync(staticSeedPath, @"INSERT INTO dbo.LoadHarness (Payload)
VALUES (N'alpha'), (N'beta'), (N'gamma');
GO
UPDATE dbo.LoadHarness SET Payload = Payload + N'-updated';
GO");

        var options = LoadHarnessOptions.Create(
            _fixture.DatabaseConnectionString,
            safeScriptPath,
            remediationScriptPath: null,
            staticSeedScriptPaths: new[] { staticSeedPath },
            reportOutputPath: reportPath,
            commandTimeoutSeconds: 60);

        var runner = new LoadHarnessRunner(_fileSystem, TimeProvider.System);
        var report = await runner.RunAsync(options).ConfigureAwait(false);

        report.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
        report.Scripts.Should().HaveCount(2);
        foreach (var script in report.Scripts)
        {
            script.BatchCount.Should().BeGreaterThan(0);
            script.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            script.BatchTimings.Length.Should().Be(script.BatchCount);
            script.Warnings.Should().BeEmpty();
        }

        var writer = new LoadHarnessReportWriter(_fileSystem);
        await writer.WriteAsync(report, reportPath).ConfigureAwait(false);

        _fileSystem.File.Exists(reportPath).Should().BeTrue();
        var json = await _fileSystem.File.ReadAllTextAsync(reportPath).ConfigureAwait(false);
        json.Should().Contain("\"Scripts\"");
    }
}
