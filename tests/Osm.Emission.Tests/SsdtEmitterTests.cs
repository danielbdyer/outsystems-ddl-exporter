using System;
using System.IO;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Emission.Tests;

public class SsdtEmitterTests
{
    [Fact]
    public async Task EmitAsync_writes_per_table_artifacts_and_manifest()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        Assert.Equal(4, manifest.Tables.Count);
        Assert.Equal(options.Emission.IncludePlatformAutoIndexes, manifest.Options.IncludePlatformAutoIndexes);
        Assert.NotNull(manifest.PolicySummary);
        Assert.Equal(report.TightenedColumnCount, manifest.PolicySummary!.TightenedColumnCount);

        var customerTable = manifest.Tables.Single(t => t.Table.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        var customerPath = Path.Combine(temp.Path, customerTable.TableFile);
        Assert.True(File.Exists(customerPath));
        var customerScript = await File.ReadAllTextAsync(customerPath);
        Assert.Contains("CREATE TABLE dbo.OSUSR_ABC_CUSTOMER", customerScript);
        Assert.Contains("PRIMARY KEY", customerScript);
        Assert.DoesNotContain("CREATE INDEX", customerScript);

        foreach (var entry in manifest.Tables)
        {
            Assert.True(File.Exists(Path.Combine(temp.Path, entry.TableFile)));
            foreach (var index in entry.IndexFiles)
            {
                var indexPath = Path.Combine(temp.Path, index);
                Assert.True(File.Exists(indexPath));
                var indexScript = await File.ReadAllTextAsync(indexPath);
                Assert.Contains("CREATE", indexScript, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("INDEX", indexScript, StringComparison.OrdinalIgnoreCase);
            }
            foreach (var fk in entry.ForeignKeyFiles)
            {
                Assert.True(File.Exists(Path.Combine(temp.Path, fk)));
            }
            Assert.Null(entry.ConcatenatedFile);
        }

        var manifestJsonPath = Path.Combine(temp.Path, "manifest.json");
        Assert.True(File.Exists(manifestJsonPath));
        var manifestJson = await File.ReadAllTextAsync(manifestJsonPath);
        Assert.Contains("OSUSR_ABC_CUSTOMER", manifestJson);
    }

    [Fact]
    public async Task EmitAsync_includes_concatenated_file_when_enabled()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission) with { EmitConcatenatedConstraints = true };
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        var customerTable = manifest.Tables.Single(t => t.Table.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(customerTable.ConcatenatedFile);
        var concatenatedPath = Path.Combine(temp.Path, customerTable.ConcatenatedFile!);
        Assert.True(File.Exists(concatenatedPath));
        var concatenated = await File.ReadAllTextAsync(concatenatedPath);
        Assert.Contains("CREATE TABLE", concatenated);
        Assert.Contains("GO", concatenated);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
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
