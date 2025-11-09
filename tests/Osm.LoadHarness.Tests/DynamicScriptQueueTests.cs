using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Osm.LoadHarness;
using System.IO.Abstractions.TestingHelpers;

namespace Osm.LoadHarness.Tests;

public sealed class DynamicScriptQueueTests
{
    [Fact]
    public void BuildScriptQueue_AppendsDynamicScripts()
    {
        var options = LoadHarnessOptions.Create(
            connectionString: "Server=localhost;Database=Harness;Trusted_Connection=True;",
            safeScriptPath: "safe.sql",
            remediationScriptPath: "remediation.sql",
            staticSeedScriptPaths: new[] { "seed.sql" },
            dynamicInsertScriptPaths: new[] { "dynamic-1.sql", "dynamic-2.sql" },
            reportOutputPath: "report.json",
            commandTimeoutSeconds: 30);

        var buildScriptQueue = typeof(LoadHarnessRunner).GetMethod(
            "BuildScriptQueue",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var queue = (IReadOnlyList<(ScriptReplayCategory Category, string Path)>)buildScriptQueue.Invoke(null, new object?[] { options })!;

        queue.Should().ContainInOrder(
            (ScriptReplayCategory.Safe, "safe.sql"),
            (ScriptReplayCategory.Remediation, "remediation.sql"),
            (ScriptReplayCategory.StaticSeed, "seed.sql"),
            (ScriptReplayCategory.Dynamic, "dynamic-1.sql"),
            (ScriptReplayCategory.Dynamic, "dynamic-2.sql"));
    }

    [Fact]
    public async Task WriteAsync_PreservesDynamicCategoriesInReport()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), "/");
        var writer = new LoadHarnessReportWriter(fileSystem);
        var reportPath = "/reports/load-harness.json";

        var dynamicResult = new ScriptReplayResult(
            ScriptReplayCategory.Dynamic,
            "dynamic.sql",
            BatchCount: 1,
            Duration: TimeSpan.FromSeconds(2),
            BatchTimings: ImmutableArray.Create(new BatchTiming(1, TimeSpan.FromSeconds(2))),
            WaitStats: ImmutableArray<WaitStatDelta>.Empty,
            LockSummary: ImmutableArray<LockSummaryEntry>.Empty,
            IndexFragmentation: ImmutableArray<IndexFragmentationEntry>.Empty,
            Warnings: ImmutableArray<string>.Empty);

        var report = new LoadHarnessReport(
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(1),
            Scripts: ImmutableArray.Create(dynamicResult),
            TotalDuration: TimeSpan.FromMinutes(1));

        await writer.WriteAsync(report, reportPath);

        var json = fileSystem.File.ReadAllText(reportPath);
        json.Should().Contain("\"Category\": \"Dynamic\"");
    }
}
