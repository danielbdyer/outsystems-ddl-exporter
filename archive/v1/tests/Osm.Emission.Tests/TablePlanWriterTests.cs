using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Abstractions.TestingHelpers;
using Osm.Domain.Model.Artifacts;
using Osm.Emission;

namespace Osm.Emission.Tests;

public class TablePlanWriterTests
{
    [Fact]
    public async Task WriteAsync_writes_plan_content_and_appends_newline()
    {
        var fileSystem = CreateFileSystem();
        var writer = new TablePlanWriter(fileSystem);
        var root = fileSystem.Directory.GetCurrentDirectory();
        var outputPath = fileSystem.Path.Combine(root, "Modules", "Sample", "dbo.Sample.sql");
        var snapshot = CreateSnapshot("Sample", "dbo", "Sample", "Modules/Sample/dbo.Sample.sql");
        var plan = new TableEmissionPlan(snapshot, outputPath, "SELECT 1");

        await writer.WriteAsync(new[] { plan }, moduleParallelism: 1, CancellationToken.None);

        var content = fileSystem.File.ReadAllText(outputPath);
        Assert.Equal("SELECT 1" + Environment.NewLine, content);
    }

    [Fact]
    public async Task WriteAsync_respects_parallelism_when_multiple_plans_are_provided()
    {
        var fileSystem = CreateFileSystem();
        var writer = new TablePlanWriter(fileSystem);
        var root = fileSystem.Directory.GetCurrentDirectory();
        var plans = new[]
        {
            CreatePlan(fileSystem, root, "One"),
            CreatePlan(fileSystem, root, "Two"),
        };

        await writer.WriteAsync(plans, moduleParallelism: 8, CancellationToken.None);

        foreach (var plan in plans)
        {
            Assert.True(fileSystem.File.Exists(plan.Path));
        }
    }

    [Fact]
    public async Task WriteAsync_skips_when_content_is_unchanged()
    {
        var root = OperatingSystem.IsWindows() ? @"c:\" : "/";
        var existingPath = System.IO.Path.Combine(root, "Modules", "Sample", "dbo.Sample.sql");
        var fileSystem = CreateFileSystem(new Dictionary<string, MockFileData>
        {
            [existingPath] = new MockFileData("SELECT 1" + Environment.NewLine),
        });

        var writer = new TablePlanWriter(fileSystem);
        var snapshot = CreateSnapshot("Sample", "dbo", "Sample", "Modules/Sample/dbo.Sample.sql");
        var plan = new TableEmissionPlan(snapshot, existingPath, "SELECT 1");

        await writer.WriteAsync(new[] { plan }, moduleParallelism: 1, CancellationToken.None);

        var content = fileSystem.File.ReadAllText(existingPath);
        Assert.Equal("SELECT 1" + Environment.NewLine, content);
    }

    private static TableEmissionPlan CreatePlan(MockFileSystem fileSystem, string root, string name)
    {
        var path = fileSystem.Path.Combine(root, "Modules", "Sample", $"dbo.{name}.sql");
        var snapshot = CreateSnapshot("Sample", "dbo", name, $"Modules/Sample/dbo.{name}.sql");
        return new TableEmissionPlan(snapshot, path, $"SELECT '{name}'");
    }

    private static MockFileSystem CreateFileSystem(IDictionary<string, MockFileData>? files = null)
    {
        var root = OperatingSystem.IsWindows() ? @"c:\" : "/";
        return new MockFileSystem(files ?? new Dictionary<string, MockFileData>(), root);
    }

    private static TableArtifactSnapshot CreateSnapshot(
        string module,
        string schema,
        string table,
        string tableFile)
    {
        var identity = TableArtifactIdentity.Create(module, module, schema, table, table, null);
        var metadata = TableArtifactMetadata.Create(null);
        var snapshot = TableArtifactSnapshot.Create(
            identity,
            Array.Empty<TableColumnSnapshot>(),
            Array.Empty<TableIndexSnapshot>(),
            Array.Empty<TableForeignKeySnapshot>(),
            Array.Empty<TableTriggerSnapshot>(),
            metadata);

        var emission = TableArtifactEmissionMetadata.Create(
            table,
            tableFile,
            Array.Empty<string>(),
            Array.Empty<string>(),
            includesExtendedProperties: false);

        return snapshot.WithEmission(emission);
    }
}
