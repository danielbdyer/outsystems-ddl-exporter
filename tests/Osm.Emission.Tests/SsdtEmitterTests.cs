using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Emission.Tests;

public class SsdtEmitterTests
{
    private static readonly SsdtEmissionMetadata DefaultMetadata = new("SHA256", "test-hash");

    [Fact]
    public async Task EmitAsync_writes_per_table_artifacts_using_mock_file_system()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), @"c:\");
        var outputDirectory = fileSystem.Path.Combine(@"c:\", "out");
        var emitter = new SsdtEmitter(new PerTableWriter(), fileSystem);
        var model = CreateMinimalModel();
        var options = SmoBuildOptions.Default;

        var manifest = await emitter.EmitAsync(model, outputDirectory, options, DefaultMetadata).ConfigureAwait(false);

        var table = Assert.Single(manifest.Tables);
        Assert.Equal("SampleModule", table.Module);
        Assert.Equal("dbo", table.Schema);
        Assert.Equal("Sample", table.Table);
        Assert.Equal("Modules/SampleModule/Tables/dbo.Sample.sql", table.TableFile);
        Assert.Empty(table.Indexes);
        Assert.Empty(table.ForeignKeys);

        var manifestPath = fileSystem.Path.Combine(outputDirectory, "manifest.json");
        Assert.True(fileSystem.File.Exists(manifestPath));
        var manifestJson = fileSystem.File.ReadAllText(manifestPath);
        Assert.Contains("\"Sample\"", manifestJson, StringComparison.Ordinal);

        var tablePath = Combine(fileSystem, outputDirectory, "Modules", "SampleModule", "Tables", "dbo.Sample.sql");
        Assert.True(fileSystem.File.Exists(tablePath));
        var script = fileSystem.File.ReadAllText(tablePath);
        Assert.Contains("CREATE TABLE [dbo].[Sample]", script, StringComparison.Ordinal);
        Assert.Contains("[Id] INT NOT NULL", script, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmitAsync_requires_non_empty_output_directory(string? outputDirectory)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), @"c:\");
        var emitter = new SsdtEmitter(new PerTableWriter(), fileSystem);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => emitter.EmitAsync(CreateMinimalModel(), outputDirectory!, SmoBuildOptions.Default, DefaultMetadata)).ConfigureAwait(false);

        Assert.Equal("outputDirectory", exception.ParamName);
    }

    [Fact]
    public async Task EmitAsync_honors_cancellation_token()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), @"c:\");
        var emitter = new SsdtEmitter(new PerTableWriter(), fileSystem);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => emitter.EmitAsync(CreateMinimalModel(), fileSystem.Path.Combine(@"c:\", "out"), SmoBuildOptions.Default, DefaultMetadata, cancellationToken: cts.Token)).ConfigureAwait(false);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EmitAsync_end_to_end_fixture_snapshot()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, DefaultMetadata, report).ConfigureAwait(false);

        Assert.Equal(4, manifest.Tables.Count);
        Assert.Equal("manifest.json", Path.GetFileName(temp.GetFiles("manifest.json").Single()));
        foreach (var entry in manifest.Tables)
        {
            var scriptPath = Path.Combine(temp.Path, entry.TableFile);
            Assert.True(File.Exists(scriptPath));
            var script = await File.ReadAllTextAsync(scriptPath).ConfigureAwait(false);
            Assert.Contains("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task EmitAsync_trusts_policy_foreign_keys_and_is_idempotent()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();

        var firstManifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, DefaultMetadata, report).ConfigureAwait(false);
        var tableWithForeignKey = firstManifest.Tables.First(entry => entry.ForeignKeys.Count > 0);
        var scriptPath = Path.Combine(temp.Path, tableWithForeignKey.TableFile);
        var firstScript = await File.ReadAllTextAsync(scriptPath).ConfigureAwait(false);
        Assert.Contains("WITH CHECK", firstScript, StringComparison.OrdinalIgnoreCase);

        var secondManifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, DefaultMetadata, report).ConfigureAwait(false);
        var secondScript = await File.ReadAllTextAsync(scriptPath).ConfigureAwait(false);

        Assert.Equal(firstManifest.Tables.Count, secondManifest.Tables.Count);
        Assert.Equal(firstScript, secondScript);
    }

    private static SmoModel CreateMinimalModel()
    {
        var columns = ImmutableArray.Create(
            new SmoColumnDefinition(
                Name: "Id",
                LogicalName: "Id",
                DataType: DataType.Int,
                Nullable: false,
                IsIdentity: true,
                IdentitySeed: 1,
                IdentityIncrement: 1,
                IsComputed: false,
                ComputedExpression: null,
                DefaultExpression: null,
                Collation: null,
                Description: null,
                DefaultConstraint: null,
                CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty));

        var table = new SmoTableDefinition(
            Module: "SampleModule",
            OriginalModule: "SampleModule",
            Name: "Sample",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Sample",
            Description: null,
            Columns: columns,
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        return new SmoModel(ImmutableArray.Create(table));
    }

    private static string Combine(IFileSystem fileSystem, string root, params string[] segments)
    {
        var path = root;
        foreach (var segment in segments)
        {
            path = fileSystem.Path.Combine(path, segment);
        }

        return path;
    }
}
