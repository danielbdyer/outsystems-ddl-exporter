using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
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
        Assert.Equal(report.UniqueIndexCount, manifest.PolicySummary!.UniqueIndexCount);
        Assert.Equal(report.UniqueIndexesEnforcedCount, manifest.PolicySummary!.UniqueIndexesEnforcedCount);

        var customerTable = manifest.Tables.Single(t => t.Table.Equals("Customer", StringComparison.Ordinal));
        var customerPath = Path.Combine(temp.Path, customerTable.TableFile);
        Assert.True(File.Exists(customerPath));
        var customerScript = await File.ReadAllTextAsync(customerPath);
        Assert.Contains("CREATE TABLE dbo.Customer", customerScript);
        Assert.Contains("PRIMARY KEY", customerScript);
        Assert.DoesNotContain("CREATE INDEX", customerScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LegacyCode", customerScript, StringComparison.Ordinal);

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
        Assert.Contains("Customer", manifestJson);
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

        var customerTable = manifest.Tables.Single(t => t.Table.Equals("Customer", StringComparison.Ordinal));
        Assert.NotNull(customerTable.ConcatenatedFile);
        var concatenatedPath = Path.Combine(temp.Path, customerTable.ConcatenatedFile!);
        Assert.True(File.Exists(concatenatedPath));
        var concatenated = await File.ReadAllTextAsync(concatenatedPath);
        Assert.Contains("CREATE TABLE", concatenated);
        Assert.Contains("GO", concatenated);
    }

    [Fact]
    public async Task EmitAsync_skips_platform_auto_indexes_when_option_disabled()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, null);

        var jobRun = manifest.Tables.Single(t => t.Table.Equals("JobRun", StringComparison.Ordinal));
        Assert.DoesNotContain(jobRun.IndexFiles, path => path.Contains("OSIDX", StringComparison.OrdinalIgnoreCase));

        var emittedOsidx = Directory.GetFiles(temp.Path, "*OSIDX*.sql", SearchOption.AllDirectories);
        Assert.Empty(emittedOsidx);
    }

    [Fact]
    public async Task EmitAsync_includes_platform_auto_indexes_when_enabled()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = CreateOptionsWithPlatformIndexesEnabled();
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, null);

        var jobRun = manifest.Tables.Single(t => t.Table.Equals("JobRun", StringComparison.Ordinal));
        var osidxPath = jobRun.IndexFiles.Single(path => path.Contains("OSIDX_JobRun_CreatedOn", StringComparison.OrdinalIgnoreCase));

        var fullPath = Path.Combine(temp.Path, osidxPath);
        Assert.True(File.Exists(fullPath));
        var script = await File.ReadAllTextAsync(fullPath);
        Assert.Contains("CREATE INDEX", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAsync_honors_unique_index_policy_flags()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var policy = new TighteningPolicy();
        var defaults = TighteningOptions.Default;
        var smoOptions = SmoBuildOptions.FromEmission(defaults.Emission);
        var factory = new SmoModelFactory();
        var emitter = new SsdtEmitter();

        using var temp = new TempDirectory();

        var cleanSnapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var cleanDecisions = policy.Decide(model, cleanSnapshot, defaults);
        var cleanOut = Path.Combine(temp.Path, "clean");
        Directory.CreateDirectory(cleanOut);
        var cleanModel = factory.Create(model, cleanDecisions, smoOptions);
        var cleanManifest = await emitter.EmitAsync(cleanModel, cleanOut, smoOptions, null);
        var cleanIndexRelative = cleanManifest.Tables
            .SelectMany(t => t.IndexFiles)
            .Single(path => path.EndsWith("UX_User_Email.sql", StringComparison.OrdinalIgnoreCase));
        var cleanIndexPath = Path.Combine(cleanOut, cleanIndexRelative);
        var cleanScript = await File.ReadAllTextAsync(cleanIndexPath);
        Assert.Contains("CREATE UNIQUE INDEX", cleanScript, StringComparison.OrdinalIgnoreCase);

        var duplicateSnapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var duplicateDecisions = policy.Decide(model, duplicateSnapshot, defaults);
        var duplicateOut = Path.Combine(temp.Path, "duplicates");
        Directory.CreateDirectory(duplicateOut);
        var duplicateModel = factory.Create(model, duplicateDecisions, smoOptions);
        var duplicateManifest = await emitter.EmitAsync(duplicateModel, duplicateOut, smoOptions, null);
        var duplicateIndexRelative = duplicateManifest.Tables
            .SelectMany(t => t.IndexFiles)
            .Single(path => path.EndsWith("UX_User_Email.sql", StringComparison.OrdinalIgnoreCase));
        var duplicateIndexPath = Path.Combine(duplicateOut, duplicateIndexRelative);
        var duplicateScript = await File.ReadAllTextAsync(duplicateIndexPath);
        Assert.Contains("CREATE INDEX", duplicateScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", duplicateScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmitAsync_applies_table_override_across_artifacts()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);

        var overrideResult = TableNamingOverride.Create("dbo", "OSUSR_ABC_CUSTOMER", "CUSTOMER_PORTAL");
        Assert.True(overrideResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrides.IsSuccess);

        var smoOptions = SmoBuildOptions.FromEmission(options.Emission).WithNamingOverrides(overrides.Value);
        var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        var renamedEntry = manifest.Tables.FirstOrDefault(t => t.Table.Equals("CUSTOMER_PORTAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(renamedEntry);
        Assert.Equal($"dbo.CUSTOMER_PORTAL.sql", Path.GetFileName(renamedEntry!.TableFile));

        var tablePath = Path.Combine(temp.Path, renamedEntry.TableFile);
        var tableScript = await File.ReadAllTextAsync(tablePath);
        Assert.Contains("CREATE TABLE dbo.CUSTOMER_PORTAL", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PK_CUSTOMER_PORTAL", tableScript, StringComparison.OrdinalIgnoreCase);

        foreach (var indexFile in renamedEntry.IndexFiles)
        {
            Assert.Contains("CUSTOMER_PORTAL", indexFile, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var fkFile in renamedEntry.ForeignKeyFiles)
        {
            Assert.Contains("CUSTOMER_PORTAL", fkFile, StringComparison.OrdinalIgnoreCase);
        }

        var allSqlFiles = Directory.GetFiles(temp.Path, "*.sql", SearchOption.AllDirectories);
        Assert.Contains(allSqlFiles, path =>
        {
            var content = File.ReadAllText(path);
            return content.Contains("CUSTOMER_PORTAL", StringComparison.OrdinalIgnoreCase);
        });

        foreach (var sqlPath in allSqlFiles)
        {
            var content = File.ReadAllText(sqlPath);
            Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task EmitAsync_applies_entity_override_across_artifacts()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);

        var overrideResult = EntityNamingOverride.Create(null, "Customer", "CUSTOMER_EXTERNAL");
        Assert.True(overrideResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(null, new[] { overrideResult.Value });
        Assert.True(overrides.IsSuccess);

        var smoOptions = SmoBuildOptions.FromEmission(options.Emission).WithNamingOverrides(overrides.Value);
        var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        var renamedEntry = manifest.Tables.FirstOrDefault(t => t.Table.Equals("CUSTOMER_EXTERNAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(renamedEntry);
        Assert.Equal($"dbo.CUSTOMER_EXTERNAL.sql", Path.GetFileName(renamedEntry!.TableFile));

        var tablePath = Path.Combine(temp.Path, renamedEntry.TableFile);
        var tableScript = await File.ReadAllTextAsync(tablePath);
        Assert.Contains("CREATE TABLE dbo.CUSTOMER_EXTERNAL", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", tableScript, StringComparison.OrdinalIgnoreCase);

        foreach (var indexFile in renamedEntry.IndexFiles)
        {
            Assert.Contains("CUSTOMER_EXTERNAL", indexFile, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var fkFile in renamedEntry.ForeignKeyFiles)
        {
            Assert.Contains("CUSTOMER_EXTERNAL", fkFile, StringComparison.OrdinalIgnoreCase);
        }

        var allSqlFiles = Directory.GetFiles(temp.Path, "*.sql", SearchOption.AllDirectories);
        foreach (var sqlPath in allSqlFiles)
        {
            var content = File.ReadAllText(sqlPath);
            Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task EmitAsync_applies_module_scoped_entity_override_with_sanitized_module_name()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var module = model.Modules.First(m => string.Equals(m.Name.Value, "AppCore", StringComparison.OrdinalIgnoreCase));

        var renamedModuleName = ModuleName.Create("App Core");
        Assert.True(renamedModuleName.IsSuccess);

        var renamedEntities = module.Entities
            .Select(e => e with { Module = renamedModuleName.Value })
            .ToImmutableArray();
        var mutatedModule = module with { Name = renamedModuleName.Value, Entities = renamedEntities };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(mutatedModel, snapshot, options);
        var report = PolicyDecisionReporter.Create(decisions);

        var overrideResult = EntityNamingOverride.Create("App Core", "Customer", "CUSTOMER_EXTERNAL");
        Assert.True(overrideResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(null, new[] { overrideResult.Value });
        Assert.True(overrides.IsSuccess);

        var smoOptions = SmoBuildOptions.FromEmission(options.Emission).WithNamingOverrides(overrides.Value);
        var smoModel = new SmoModelFactory().Create(mutatedModel, decisions, smoOptions);

        using var temp = new TempDirectory();
        var emitter = new SsdtEmitter();
        var manifest = await emitter.EmitAsync(smoModel, temp.Path, smoOptions, report);

        var renamedEntry = manifest.Tables.FirstOrDefault(
            t => t.Table.Equals("CUSTOMER_EXTERNAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(renamedEntry);
        Assert.Contains("Modules/App_Core", renamedEntry!.TableFile, StringComparison.OrdinalIgnoreCase);

        var tablePath = Path.Combine(temp.Path, renamedEntry.TableFile);
        var tableScript = await File.ReadAllTextAsync(tablePath);
        Assert.Contains("CREATE TABLE dbo.CUSTOMER_EXTERNAL", tableScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", tableScript, StringComparison.OrdinalIgnoreCase);
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

    private static TighteningOptions CreateOptionsWithPlatformIndexesEnabled()
    {
        var defaults = TighteningOptions.Default;
        var emissionResult = EmissionOptions.Create(
            defaults.Emission.PerTableFiles,
            includePlatformAutoIndexes: true,
            defaults.Emission.SanitizeModuleNames,
            defaults.Emission.EmitConcatenatedConstraints,
            defaults.Emission.NamingOverrides);

        Assert.True(emissionResult.IsSuccess);

        var optionsResult = TighteningOptions.Create(
            defaults.Policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            emissionResult.Value,
            defaults.Mocking);

        Assert.True(optionsResult.IsSuccess);
        return optionsResult.Value;
    }
}
