using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Osm.Pipeline.StaticData;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
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
        var expectedRoot = Path.Combine(repoRoot, "tests", "Fixtures", "emission", "edge-case");

        var tighteningOptions = OverrideModuleParallelism(await LoadTighteningOptionsAsync(configPath), 2);
        var model = await LoadModelAsync(modelPath);
        var profile = await LoadProfileAsync(profilePath);

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, profile, tighteningOptions);
        var decisionReport = PolicyDecisionReporter.Create(decisions);

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission);
        var smoModel = new SmoModelFactory().Create(
            model,
            decisions,
            profile: profile,
            options: smoOptions);

        using var output = new TempDirectory();
        var emitter = new Osm.Emission.SsdtEmitter();
        var logWriter = new PolicyDecisionLogWriter();
        var fingerprintCalculator = new EmissionFingerprintCalculator();
        var metadata = fingerprintCalculator.Compute(smoModel, decisions, smoOptions);
        var coverageResult = EmissionCoverageCalculator.Compute(
            model,
            ImmutableArray.Create(OutSystemsInternalModel.Users),
            decisions,
            smoModel,
            smoOptions);

        await emitter.EmitAsync(
            smoModel,
            output.Path,
            smoOptions,
            metadata,
            decisionReport,
            coverage: coverageResult.Summary,
            unsupported: coverageResult.Unsupported);

        await logWriter.WriteAsync(output.Path, decisionReport);

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

        DirectorySnapshot.AssertMatches(expectedRoot, output.Path);
    }

    [Fact]
    public async Task BuildSsdtPipeline_WithRenamesMatchesFixtures()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var modelPath = Path.Combine(repoRoot, "tests", "Fixtures", "model.edge-case.json");
        var profilePath = Path.Combine(repoRoot, "tests", "Fixtures", "profiling", "profile.edge-case.json");
        var configPath = Path.Combine(repoRoot, "config", "default-tightening.json");
        var expectedRoot = Path.Combine(repoRoot, "tests", "Fixtures", "emission", "edge-case-rename");

        var tighteningOptions = OverrideModuleParallelism(await LoadTighteningOptionsAsync(configPath), 2);
        var model = await LoadModelAsync(modelPath);
        var profile = await LoadProfileAsync(profilePath);

        var overrideResult = NamingOverrideRule.Create("dbo", "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_PORTAL");
        Assert.True(overrideResult.IsSuccess, string.Join(Environment.NewLine, overrideResult.Errors.Select(e => e.Message)));

        var overrideOptions = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrideOptions.IsSuccess, string.Join(Environment.NewLine, overrideOptions.Errors.Select(e => e.Message)));

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, profile, tighteningOptions);
        var decisionReport = PolicyDecisionReporter.Create(decisions);

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
        var logWriter = new PolicyDecisionLogWriter();
        var fingerprintCalculator = new EmissionFingerprintCalculator();
        var metadata = fingerprintCalculator.Compute(smoModel, decisions, smoOptions);
        var coverageResult = EmissionCoverageCalculator.Compute(
            model,
            ImmutableArray.Create(OutSystemsInternalModel.Users),
            decisions,
            smoModel,
            smoOptions);

        await emitter.EmitAsync(
            smoModel,
            output.Path,
            smoOptions,
            metadata,
            decisionReport,
            coverage: coverageResult.Summary,
            unsupported: coverageResult.Unsupported);

        await logWriter.WriteAsync(output.Path, decisionReport);

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

        DirectorySnapshot.AssertMatches(expectedRoot, output.Path);
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

}
