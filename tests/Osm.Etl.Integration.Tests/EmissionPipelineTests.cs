using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Pipeline.StaticData;
using Osm.Domain.Configuration;
using Osm.Json;
using Osm.Json.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Etl.Integration.Tests;

public class EmissionPipelineTests
{
    [Fact]
    public async Task BuildSsdtPipeline_MatchesEdgeCaseFixtures()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var modelPath = Path.Combine(repoRoot, "tests", "Fixtures", "model.edge-case.json");
        var profilePath = Path.Combine(repoRoot, "tests", "Fixtures", "profiling", "profile.edge-case.json");
        var configPath = Path.Combine(repoRoot, "config", "default-tightening.json");
        var tighteningOptions = OverrideModuleParallelism(await LoadTighteningOptionsAsync(configPath), 2);
        var model = await LoadModelAsync(modelPath);
        var profile = await LoadProfileAsync(profilePath);

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, profile, tighteningOptions);
        var predicates = ModelPredicateEvaluator.Evaluate(model, decisions);
        var decisionReport = PolicyDecisionReporter.Create(decisions, predicates);

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission);
        var smoModel = new SmoModelFactory().Create(
            model,
            decisions,
            profile: profile,
            options: smoOptions);

        using var output = new TempDirectory();
        var emitter = new Osm.Emission.SsdtEmitter();
        var fingerprintCalculator = new EmissionFingerprintCalculator();
        var metadata = fingerprintCalculator.Compute(smoModel, decisions, smoOptions);
        await emitter.EmitAsync(smoModel, output.Path, smoOptions, metadata, decisionReport);
        await WriteDecisionLogAsync(output.Path, decisionReport);

        var staticDefinitions = StaticEntitySeedDefinitionBuilder.Build(model, smoOptions.NamingOverrides);
        if (!staticDefinitions.IsDefaultOrEmpty)
        {
            var seedPath = Path.Combine(repoRoot, "tests", "Fixtures", "static-data", "static-entities.edge-case.json");
            var provider = new FixtureStaticEntityDataProvider(seedPath);
            var seedResult = await provider.GetDataAsync(staticDefinitions);
            AssertResultSucceeded(seedResult);

        var template = StaticEntitySeedTemplate.Load();
        var generator = new StaticEntitySeedScriptGenerator();
        var seedsRoot = Path.Combine(output.Path, "Seeds");
        Directory.CreateDirectory(seedsRoot);
        var deterministicSeeds = StaticEntitySeedDeterminizer.Normalize(seedResult.Value);
        var seedGroups = deterministicSeeds
            .GroupBy(data => data.Definition.Module, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in seedGroups)
        {
            var sanitizedModule = smoOptions.SanitizeModuleNames
                ? ModuleNameSanitizer.Sanitize(group.Key)
                : group.Key;
            var moduleDirectory = Path.Combine(seedsRoot, sanitizedModule);
            Directory.CreateDirectory(moduleDirectory);
            await generator.WriteAsync(
                Path.Combine(moduleDirectory, "StaticEntities.seed.sql"),
                template,
                group.ToArray(),
                StaticSeedSynchronizationMode.NonDestructive);
        }
        }

        var emission = EmissionOutput.Load(output.Path);

        Assert.True(emission.Manifest.Options.SanitizeModuleNames);
        Assert.Equal(2, emission.Manifest.Options.ModuleParallelism);

        Assert.Equal(4, emission.Manifest.Tables.Count);

        var tableNames = emission.Manifest.Tables.Select(table => table.Table).ToArray();
        Assert.Contains("Customer", tableNames);
        Assert.Contains("City", tableNames);
        Assert.Contains("BillingAccount", tableNames);
        Assert.Contains("JobRun", tableNames);

        var customer = Assert.Single(emission.Manifest.Tables.Where(t => t.Table == "Customer"));
        Assert.Equal("AppCore", customer.Module);
        Assert.Equal("dbo", customer.Schema);
        Assert.Equal("Modules/AppCore/Tables/dbo.Customer.sql", customer.TableFile);
        Assert.Contains("FK_Customer_CityId", customer.ForeignKeys);
        Assert.Contains("IDX_Customer_Email", customer.Indexes);

        Assert.Equal(emission.Manifest.Tables.Count, emission.Manifest.Coverage.Tables.Emitted);
        Assert.Equal(
            emission.Manifest.Tables.Count + emission.Manifest.Unsupported.Count,
            emission.Manifest.Coverage.Tables.Total);
        Assert.Equal(emission.Manifest.Coverage.Columns.Emitted, emission.Manifest.Coverage.Columns.Total);
        Assert.True(emission.Manifest.PolicySummary.ColumnCount >= emission.Manifest.Coverage.Columns.Total);

        Assert.Equal(15, emission.Manifest.PolicySummary.ColumnCount);
        Assert.Equal(10, emission.Manifest.PolicySummary.TightenedColumnCount);
        Assert.Equal(2, emission.Manifest.PolicySummary.UniqueIndexCount);
        Assert.Equal(1, emission.Manifest.PolicySummary.ForeignKeysCreatedCount);

        if (emission.Manifest.Unsupported.Count > 0)
        {
            Assert.Contains(
                "Table dbo.OSUSR_U_USER missing from emission output.",
                emission.Manifest.Unsupported);
        }

        var seedModule = Assert.Single(emission.StaticSeedModules);
        Assert.Equal("AppCore", seedModule.Module);
        Assert.Contains("Seeds/AppCore/StaticEntities.seed.sql", seedModule.SeedFiles);

        Assert.Contains("Modules/AppCore/Tables/dbo.Customer.sql", emission.TableScripts);
        Assert.Contains("Modules/ExtBilling/Tables/billing.BillingAccount.sql", emission.TableScripts);
    }

    [Fact]
    public async Task BuildSsdtPipeline_WithRenamesMatchesFixtures()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var modelPath = Path.Combine(repoRoot, "tests", "Fixtures", "model.edge-case.json");
        var profilePath = Path.Combine(repoRoot, "tests", "Fixtures", "profiling", "profile.edge-case.json");
        var configPath = Path.Combine(repoRoot, "config", "default-tightening.json");
        var tighteningOptions = OverrideModuleParallelism(await LoadTighteningOptionsAsync(configPath), 2);
        var model = await LoadModelAsync(modelPath);
        var profile = await LoadProfileAsync(profilePath);

        var overrideResult = NamingOverrideRule.Create("dbo", "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_PORTAL");
        Assert.True(overrideResult.IsSuccess, string.Join(Environment.NewLine, overrideResult.Errors.Select(e => e.Message)));

        var overrideOptions = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrideOptions.IsSuccess, string.Join(Environment.NewLine, overrideOptions.Errors.Select(e => e.Message)));

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, profile, tighteningOptions);
        var predicates = ModelPredicateEvaluator.Evaluate(model, decisions);
        var decisionReport = PolicyDecisionReporter.Create(decisions, predicates);

        var smoOptions = SmoBuildOptions
            .FromEmission(tighteningOptions.Emission)
            .WithNamingOverrides(overrideOptions.Value);
        var smoModel = new SmoModelFactory().Create(
            model,
            decisions,
            profile: profile,
            options: smoOptions);

        using var output = new TempDirectory();
        var emitter = new Osm.Emission.SsdtEmitter();
        var fingerprintCalculator = new EmissionFingerprintCalculator();
        var metadata = fingerprintCalculator.Compute(smoModel, decisions, smoOptions);
        await emitter.EmitAsync(smoModel, output.Path, smoOptions, metadata, decisionReport);
        await WriteDecisionLogAsync(output.Path, decisionReport);

        var staticDefinitions = StaticEntitySeedDefinitionBuilder.Build(model, smoOptions.NamingOverrides);
        if (!staticDefinitions.IsDefaultOrEmpty)
        {
            var seedPath = Path.Combine(repoRoot, "tests", "Fixtures", "static-data", "static-entities.edge-case.json");
            var provider = new FixtureStaticEntityDataProvider(seedPath);
            var seedResult = await provider.GetDataAsync(staticDefinitions);
            AssertResultSucceeded(seedResult);

        var template = StaticEntitySeedTemplate.Load();
        var generator = new StaticEntitySeedScriptGenerator();
        var seedsRoot = Path.Combine(output.Path, "Seeds");
        Directory.CreateDirectory(seedsRoot);
        var deterministicSeeds = StaticEntitySeedDeterminizer.Normalize(seedResult.Value);
        var seedGroups = deterministicSeeds
            .GroupBy(data => data.Definition.Module, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in seedGroups)
        {
            var sanitizedModule = smoOptions.SanitizeModuleNames
                ? ModuleNameSanitizer.Sanitize(group.Key)
                : group.Key;
            var moduleDirectory = Path.Combine(seedsRoot, sanitizedModule);
            Directory.CreateDirectory(moduleDirectory);
            await generator.WriteAsync(
                Path.Combine(moduleDirectory, "StaticEntities.seed.sql"),
                template,
                group.ToArray(),
                StaticSeedSynchronizationMode.NonDestructive);
        }
        }

        var emission = EmissionOutput.Load(output.Path);

        var renamed = Assert.Single(emission.Manifest.Tables.Where(t => t.Table == "CUSTOMER_PORTAL"));
        Assert.Equal("AppCore", renamed.Module);
        Assert.Equal("Modules/AppCore/Tables/dbo.CUSTOMER_PORTAL.sql", renamed.TableFile);
        Assert.DoesNotContain(emission.Manifest.Tables, t => t.Table == "Customer");

        Assert.Contains("Modules/AppCore/Tables/dbo.CUSTOMER_PORTAL.sql", emission.TableScripts);
        Assert.DoesNotContain(
            emission.TableScripts,
            path => path.Equals("Modules/AppCore/Tables/dbo.Customer.sql", StringComparison.OrdinalIgnoreCase));

        var script = await File.ReadAllTextAsync(emission.GetAbsolutePath(renamed.TableFile));
        Assert.Contains("CREATE TABLE [dbo].[CUSTOMER_PORTAL]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", script, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TighteningOptions> LoadTighteningOptionsAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var result = new TighteningOptionsDeserializer().Deserialize(stream);
        AssertResultSucceeded(result);
        return result.Value;
    }

    private static async Task<Osm.Domain.Model.OsmModel> LoadModelAsync(string path)
    {
        var ingestion = new ModelIngestionService(new ModelJsonDeserializer());
        var result = await ingestion.LoadFromFileAsync(path);
        AssertResultSucceeded(result);
        return result.Value;
    }

    private static async Task<Osm.Domain.Profiling.ProfileSnapshot> LoadProfileAsync(string path)
    {
        var profiler = new FixtureDataProfiler(path, new ProfileSnapshotDeserializer());
        var result = await profiler.CaptureAsync();
        AssertResultSucceeded(result);
        return result.Value;
    }

    private static void AssertResultSucceeded<T>(Osm.Domain.Abstractions.Result<T> result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        var message = string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.Code}: {e.Message}"));
        throw new Xunit.Sdk.XunitException($"Expected result to succeed but failed with:{Environment.NewLine}{message}");
    }

    private static TighteningOptions OverrideModuleParallelism(TighteningOptions options, int moduleParallelism)
    {
        var emission = options.Emission;
        var emissionOverride = EmissionOptions.Create(
            emission.PerTableFiles,
            emission.IncludePlatformAutoIndexes,
            emission.SanitizeModuleNames,
            emission.EmitBareTableOnly,
            emission.EmitTableHeaders,
            moduleParallelism,
            emission.NamingOverrides,
            emission.StaticSeeds);
        AssertResultSucceeded(emissionOverride);

        var tightened = TighteningOptions.Create(
            options.Policy,
            options.ForeignKeys,
            options.Uniqueness,
            options.Remediation,
            emissionOverride.Value,
            options.Mocking);
        AssertResultSucceeded(tightened);

        return tightened.Value;
    }

    private static async Task WriteDecisionLogAsync(string outputDirectory, PolicyDecisionReport report)
    {
        var log = new PolicyDecisionLog(
            report.ColumnCount,
            report.TightenedColumnCount,
            report.RemediationColumnCount,
            report.UniqueIndexCount,
            report.UniqueIndexesEnforcedCount,
            report.UniqueIndexesRequireRemediationCount,
            report.ForeignKeyCount,
            report.ForeignKeysCreatedCount,
            report.ColumnRationaleCounts,
            report.UniqueIndexRationaleCounts,
            report.ForeignKeyRationaleCounts,
            report.Columns.Select(static c => new PolicyDecisionLogColumn(
                c.Column.Schema.Value,
                c.Column.Table.Value,
                c.Column.Column.Value,
                c.MakeNotNull,
                c.RequiresRemediation,
                c.Rationales.ToArray())).ToArray(),
            report.UniqueIndexes.Select(static u => new PolicyDecisionLogUniqueIndex(
                u.Index.Schema.Value,
                u.Index.Table.Value,
                u.Index.Index.Value,
                u.EnforceUnique,
                u.RequiresRemediation,
                u.Rationales.ToArray())).ToArray(),
            report.ForeignKeys.Select(static f => new PolicyDecisionLogForeignKey(
                f.Column.Schema.Value,
                f.Column.Table.Value,
                f.Column.Column.Value,
                f.CreateConstraint,
                f.Rationales.ToArray())).ToArray(),
            report.Diagnostics.Select(static d => new PolicyDecisionLogDiagnostic(
                d.LogicalName,
                d.CanonicalModule,
                d.CanonicalSchema,
                d.CanonicalPhysicalName,
                d.Code,
                d.Message,
                d.Severity.ToString(),
                d.ResolvedByOverride,
                d.Candidates.Select(static c => new PolicyDecisionLogDuplicateCandidate(
                    c.Module,
                    c.Schema,
                    c.PhysicalName)).ToArray())).ToArray());

        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "policy-decisions.json"), json);
    }

    private sealed record PolicyDecisionLog(
        int ColumnCount,
        int TightenedColumnCount,
        int RemediationColumnCount,
        int UniqueIndexCount,
        int UniqueIndexesEnforcedCount,
        int UniqueIndexesRequireRemediationCount,
        int ForeignKeyCount,
        int ForeignKeysCreatedCount,
        IReadOnlyDictionary<string, int> ColumnRationales,
        IReadOnlyDictionary<string, int> UniqueIndexRationales,
        IReadOnlyDictionary<string, int> ForeignKeyRationales,
        IReadOnlyList<PolicyDecisionLogColumn> Columns,
        IReadOnlyList<PolicyDecisionLogUniqueIndex> UniqueIndexes,
        IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys,
        IReadOnlyList<PolicyDecisionLogDiagnostic> Diagnostics);

    private sealed record PolicyDecisionLogColumn(
        string Schema,
        string Table,
        string Column,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogUniqueIndex(
        string Schema,
        string Table,
        string Index,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogForeignKey(
        string Schema,
        string Table,
        string Column,
        bool CreateConstraint,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogDiagnostic(
        string LogicalName,
        string CanonicalModule,
        string CanonicalSchema,
        string CanonicalPhysicalName,
        string Code,
        string Message,
        string Severity,
        bool ResolvedByOverride,
        IReadOnlyList<PolicyDecisionLogDuplicateCandidate> Candidates);

    private sealed record PolicyDecisionLogDuplicateCandidate(string Module, string Schema, string PhysicalName);
}
