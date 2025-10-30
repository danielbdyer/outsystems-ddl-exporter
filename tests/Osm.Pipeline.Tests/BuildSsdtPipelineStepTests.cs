using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Json;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class BuildSsdtPipelineStepTests
{
    [Fact]
    public async Task BootstrapStep_populates_state_and_logs_request()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var step = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());

        var result = await step.ExecuteAsync(initial);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.NotNull(state.Bootstrap);
        Assert.NotNull(state.Bootstrap.Profile);
        Assert.NotNull(state.Bootstrap.FilteredModel);
        Assert.False(state.Bootstrap.Insights.IsDefault);
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "request.received");
        Assert.Contains(log.Entries, entry => entry.Step == "profiling.capture.completed");
    }

    [Fact]
    public async Task EvidenceCacheStep_persists_result_and_records_metadata()
    {
        using var output = new TempDirectory();
        using var cacheDirectory = new TempDirectory();
        var manifest = new EvidenceCacheManifest(
            Version: "1.0",
            Key: "key",
            Command: "build-ssdt",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastValidatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            ModuleSelection: EvidenceCacheModuleSelection.Empty,
            Metadata: new Dictionary<string, string?>(StringComparer.Ordinal),
            Artifacts: new List<EvidenceCacheArtifact>());
        var evaluation = new EvidenceCacheEvaluation(
            EvidenceCacheOutcome.Created,
            EvidenceCacheInvalidationReason.ManifestMissing,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["cacheOutcome"] = EvidenceCacheOutcome.Created.ToString(),
            });
        var cacheResult = new EvidenceCacheResult(cacheDirectory.Path, manifest, evaluation);
        var cacheService = new FakeEvidenceCacheService(Result<EvidenceCacheResult>.Success(cacheResult));

        var cacheOptions = new EvidenceCachePipelineOptions(
            cacheDirectory.Path,
            Refresh: false,
            Command: "build-ssdt",
            ModelPath: FixtureFile.GetPath("model.edge-case.json"),
            ProfilePath: FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")),
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>());

        var request = CreateRequest(output.Path, cacheOptions: cacheOptions);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var coordinator = new EvidenceCacheCoordinator(cacheService);
        var step = new BuildSsdtEvidenceCacheStep(coordinator);

        var result = await step.ExecuteAsync(bootstrapState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.Equal(cacheResult, state.EvidenceCache);
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "evidence.cache.requested");
        Assert.Contains(log.Entries, entry => entry.Step == "evidence.cache.persisted" || entry.Step == "evidence.cache.reused");
    }

    [Fact]
    public async Task PolicyStep_synthesizes_decision_report()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var step = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());

        var result = await step.ExecuteAsync(evidenceState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.NotNull(state.Report);
        Assert.True(state.Report.ColumnCount > 0);
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "policy.decisions.synthesized");
    }

    [Fact]
    public async Task EmissionStep_writes_manifest_and_decision_log()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var step = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());

        var result = await step.ExecuteAsync(decisionState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.NotNull(state.Manifest);
        Assert.False(state.Manifest.Tables.Count == 0);
        Assert.NotNull(state.DecisionLogPath);
        Assert.True(File.Exists(Path.Combine(output.Path, "manifest.json")));
        Assert.True(File.Exists(state.DecisionLogPath));
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.emission.completed");
        Assert.Contains(log.Entries, entry => entry.Step == "policy.log.persisted");
    }

    [Fact]
    public async Task StaticSeedStep_generates_seed_scripts()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(emissionState)).Value;
        var step = new BuildSsdtStaticSeedStep(CreateSeedGenerator());

        var result = await step.ExecuteAsync(validatedState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.False(state.StaticSeedScriptPaths.IsDefaultOrEmpty);
        Assert.All(state.StaticSeedScriptPaths, path => Assert.True(File.Exists(path)));
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "staticData.seed.generated");
    }

    [Fact]
    public async Task StaticSeedStep_emits_master_seed_when_enabled()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());

        var defaults = request.Scope.TighteningOptions;
        var staticSeedOptions = StaticSeedOptions.Create(
            groupByModule: true,
            emitMasterFile: true,
            defaults.Emission.StaticSeeds.SynchronizationMode).Value;
        var emission = EmissionOptions.Create(
            defaults.Emission.PerTableFiles,
            defaults.Emission.IncludePlatformAutoIndexes,
            defaults.Emission.SanitizeModuleNames,
            defaults.Emission.EmitBareTableOnly,
            defaults.Emission.EmitTableHeaders,
            defaults.Emission.ModuleParallelism,
            defaults.Emission.NamingOverrides,
            staticSeedOptions).Value;
        var tightening = TighteningOptions.Create(
            defaults.Policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            emission,
            defaults.Mocking).Value;

        request = request with
        {
            Scope = request.Scope with
            {
                TighteningOptions = tightening,
                SmoOptions = SmoBuildOptions.FromEmission(emission)
            }
        };

        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(emissionState)).Value;
        var step = new BuildSsdtStaticSeedStep(CreateSeedGenerator());

        var result = await step.ExecuteAsync(validatedState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.Contains(Path.Combine(output.Path, "Seeds", "StaticEntities.seed.sql"), state.StaticSeedScriptPaths);
        Assert.True(state.StaticSeedScriptPaths.Length >= 2);
    }

    [Fact]
    public async Task StaticSeedStep_disambiguates_colliding_sanitized_module_names()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new CollidingStaticEntityDataProvider());
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(emissionState)).Value;
        var step = new BuildSsdtStaticSeedStep(CreateSeedGenerator());

        var result = await step.ExecuteAsync(validatedState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.Equal(2, state.StaticSeedScriptPaths.Length);
        var moduleFolders = state.StaticSeedScriptPaths
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path)!))
            .ToArray();
        Assert.False(string.Equals(moduleFolders[0], moduleFolders[1], StringComparison.OrdinalIgnoreCase));
        Assert.Contains(moduleFolders, folder => string.Equals(folder, "Module_Alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(moduleFolders, folder => string.Equals(folder, "Module_Alpha_2", StringComparison.OrdinalIgnoreCase));

        var log = state.Log.Build();
        var remapEntry = Assert.Single(log.Entries.Where(entry => entry.Step == "staticData.seed.moduleNameRemapped"));
        Assert.Equal("Module#Alpha", remapEntry.Metadata["module.originalName"]);
        Assert.Equal("Module_Alpha", remapEntry.Metadata["module.sanitizedName"]);
        Assert.Equal("Module_Alpha_2", remapEntry.Metadata["module.disambiguatedName"]);
    }

    [Fact]
    public async Task TelemetryPackagingStep_creates_archive_with_expected_artifacts()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(emissionState)).Value;
        var staticSeedStep = new BuildSsdtStaticSeedStep(CreateSeedGenerator());
        var seedState = (await staticSeedStep.ExecuteAsync(validatedState)).Value;

        var step = new BuildSsdtTelemetryPackagingStep();
        var result = await step.ExecuteAsync(seedState);

        Assert.True(result.IsSuccess);
        var packaged = result.Value;
        var packagePath = Assert.Single(packaged.TelemetryPackagePaths);
        Assert.True(File.Exists(packagePath));

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
            var expectedEntries = new[]
            {
                NormalizeRelative(output.Path, Path.Combine(output.Path, "manifest.json")),
                NormalizeRelative(output.Path, packaged.DecisionLogPath),
                NormalizeRelative(output.Path, packaged.OpportunityArtifacts.SafeScriptPath),
                NormalizeRelative(output.Path, packaged.OpportunityArtifacts.RemediationScriptPath),
            };

            foreach (var expected in expectedEntries)
            {
                Assert.Contains(expected, entries);
            }
        }

        var log = packaged.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "pipeline.execution");

        static string NormalizeRelative(string root, string path)
        {
            var absoluteRoot = Path.GetFullPath(root);
            var absolutePath = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(absoluteRoot, absolutePath);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                return Path.GetFileName(absolutePath);
            }

            return relative.Replace('\\', '/');
        }
    }

    [Fact]
    public async Task SqlValidationStep_records_summary_for_valid_scripts()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var step = new BuildSsdtSqlValidationStep();

        var result = await step.ExecuteAsync(emissionState);

        Assert.True(result.IsSuccess);
        var validated = result.Value;
        Assert.Equal(emissionState.Manifest.Tables.Count, validated.SqlValidation.TotalFiles);
        Assert.Equal(0, validated.SqlValidation.ErrorCount);
        var log = validated.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.sql.validation.completed");
        Assert.DoesNotContain(log.Entries, entry => entry.Step == "ssdt.sql.validation.error");
    }

    [Fact]
    public async Task SqlValidationStep_returns_failure_when_scripts_invalid()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        Assert.NotEmpty(emissionState.Manifest.Tables);
        var firstTable = emissionState.Manifest.Tables[0];
        var artifactPath = Path.Combine(request.OutputDirectory, firstTable.TableFile.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(artifactPath, "CREATE TABLE ???");
        var step = new BuildSsdtSqlValidationStep();

        var result = await step.ExecuteAsync(emissionState);

        Assert.True(result.IsFailure);
        var log = emissionState.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.sql.validation.completed");
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.sql.validation.error");
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.buildSsdt.sql.validationFailed", error.Code);
    }

    private static BuildSsdtPipelineRequest CreateRequest(
        string outputDirectory,
        EvidenceCachePipelineOptions? cacheOptions = null,
        IStaticEntityDataProvider? staticDataProvider = null)
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var scope = new ModelExecutionScope(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicyLoader.LoadDefault(),
            profilePath);

        return new BuildSsdtPipelineRequest(
            scope,
            outputDirectory,
            "fixture",
            cacheOptions,
            staticDataProvider,
            SeedOutputDirectoryHint: null);
    }

    private static IDataProfilerFactory CreateProfilerFactory()
    {
        return new DataProfilerFactory(
            new ProfileSnapshotDeserializer(),
            static (connectionString, options) => new SqlConnectionFactory(connectionString, options));
    }

    private static StaticEntitySeedScriptGenerator CreateSeedGenerator()
    {
        var literalFormatter = new SqlLiteralFormatter();
        var sqlBuilder = new StaticSeedSqlBuilder(literalFormatter);
        var templateService = new StaticEntitySeedTemplateService();
        return new StaticEntitySeedScriptGenerator(templateService, sqlBuilder);
    }

    private static PipelineBootstrapper CreatePipelineBootstrapper()
    {
        return new PipelineBootstrapper(
            new ModelIngestionService(new ModelJsonDeserializer()),
            new ModuleFilter(),
            new SupplementalEntityLoader(new ModelJsonDeserializer()),
            new ProfilingInsightGenerator());
    }

    private sealed class FakeEvidenceCacheService : IEvidenceCacheService
    {
        private readonly Result<EvidenceCacheResult> _result;

        public FakeEvidenceCacheService(Result<EvidenceCacheResult> result)
        {
            _result = result;
        }

        public Task<Result<EvidenceCacheResult>> CacheAsync(EvidenceCacheRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class EchoStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            var tables = definitions
                .Select(definition => StaticEntityTableData.Create(
                    definition,
                    new[]
                    {
                        StaticEntityRow.Create(GenerateValues(definition))
                    }))
                .Cast<StaticEntityTableData>()
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(tables));
        }

        private static object?[] GenerateValues(StaticEntitySeedTableDefinition definition)
        {
            var values = new object?[definition.Columns.Length];
            for (var i = 0; i < definition.Columns.Length; i++)
            {
                values[i] = i == 0 ? 1 : $"Sample{i}";
            }

            return values;
        }
    }

    private sealed class CollidingStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            if (definitions.Count == 0)
            {
                throw new InvalidOperationException("Fixture model must contain at least one static entity definition.");
            }

            var template = definitions[0];
            var tables = new[]
            {
                CreateTable(template, "Module Alpha", "Alpha"),
                CreateTable(template, "Module#Alpha", "Hash"),
            };

            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(tables));
        }

        private static StaticEntityTableData CreateTable(
            StaticEntitySeedTableDefinition template,
            string moduleName,
            string suffix)
        {
            var definition = template with
            {
                Module = moduleName,
                LogicalName = $"{template.LogicalName}_{suffix}",
                PhysicalName = $"{template.PhysicalName}_{suffix}",
                EffectiveName = $"{template.EffectiveName}_{suffix}"
            };

            return StaticEntityTableData.Create(
                definition,
                new[]
                {
                    StaticEntityRow.Create(GenerateValues(definition))
                });
        }

        private static object?[] GenerateValues(StaticEntitySeedTableDefinition definition)
        {
            var values = new object?[definition.Columns.Length];
            for (var i = 0; i < definition.Columns.Length; i++)
            {
                values[i] = i == 0 ? 1 : $"Sample{i}";
            }

            return values;
        }
    }
}
