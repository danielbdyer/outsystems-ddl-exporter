using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Abstractions.TestingHelpers;
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
        var plan = new TableEmissionPlan(
            new TableManifestEntry(
                Module: "Sample",
                Schema: "dbo",
                Table: "Sample",
                TableFile: "Modules/Sample/dbo.Sample.sql",
                Indexes: Array.Empty<string>(),
                ForeignKeys: Array.Empty<string>(),
                IncludesExtendedProperties: false),
            outputPath,
            "SELECT 1");

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
        var plan = new TableEmissionPlan(
            new TableManifestEntry(
                Module: "Sample",
                Schema: "dbo",
                Table: "Sample",
                TableFile: "Modules/Sample/dbo.Sample.sql",
                Indexes: Array.Empty<string>(),
                ForeignKeys: Array.Empty<string>(),
                IncludesExtendedProperties: false),
            existingPath,
            "SELECT 1");

        await writer.WriteAsync(new[] { plan }, moduleParallelism: 1, CancellationToken.None);

        var content = fileSystem.File.ReadAllText(existingPath);
        Assert.Equal("SELECT 1" + Environment.NewLine, content);
    }

    private static TableEmissionPlan CreatePlan(MockFileSystem fileSystem, string root, string name)
    {
        var path = fileSystem.Path.Combine(root, "Modules", "Sample", $"dbo.{name}.sql");
        return new TableEmissionPlan(
            new TableManifestEntry(
                Module: "Sample",
                Schema: "dbo",
                Table: name,
                TableFile: $"Modules/Sample/dbo.{name}.sql",
                Indexes: Array.Empty<string>(),
                ForeignKeys: Array.Empty<string>(),
                IncludesExtendedProperties: false),
            path,
            $"SELECT '{name}'");
    }

    private static MockFileSystem CreateFileSystem(IDictionary<string, MockFileData>? files = null)
    {
        var root = OperatingSystem.IsWindows() ? @"c:\" : "/";
        return new MockFileSystem(files ?? new Dictionary<string, MockFileData>(), root);
    }
}
