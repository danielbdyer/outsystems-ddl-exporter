using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Json;
using Osm.Smo;
using Osm.Validation.Tightening;
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
        var step = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), CreateProfilerFactory());

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
        var bootstrapStep = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var step = new BuildSsdtEvidenceCacheStep(cacheService);

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
        var bootstrapStep = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var step = new BuildSsdtPolicyDecisionStep(new TighteningPolicy());

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
        var bootstrapStep = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var step = new BuildSsdtEmissionStep(new SmoModelFactory(), new SsdtEmitter(), new PolicyDecisionLogWriter(), new EmissionFingerprintCalculator());

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
        var bootstrapStep = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(new SmoModelFactory(), new SsdtEmitter(), new PolicyDecisionLogWriter(), new EmissionFingerprintCalculator());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var step = new BuildSsdtStaticSeedStep(new StaticEntitySeedScriptGenerator(), StaticEntitySeedTemplate.Load());

        var result = await step.ExecuteAsync(emissionState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.False(state.StaticSeedScriptPaths.IsDefaultOrEmpty);
        Assert.All(state.StaticSeedScriptPaths, path => Assert.True(File.Exists(path)));
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "staticData.seed.generated");
    }

    private static BuildSsdtPipelineRequest CreateRequest(
        string outputDirectory,
        EvidenceCachePipelineOptions? cacheOptions = null,
        IStaticEntityDataProvider? staticDataProvider = null)
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        return new BuildSsdtPipelineRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            outputDirectory,
            TighteningOptions.Default,
            SupplementalModelOptions.Default,
            "fixture",
            profilePath,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null)),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicy.LoadDefault(),
            cacheOptions,
            staticDataProvider,
            null);
    }

    private static IDataProfilerFactory CreateProfilerFactory()
    {
        return new DataProfilerFactory(
            new ProfileSnapshotDeserializer(),
            static (connectionString, options) => new SqlConnectionFactory(connectionString, options));
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
}
