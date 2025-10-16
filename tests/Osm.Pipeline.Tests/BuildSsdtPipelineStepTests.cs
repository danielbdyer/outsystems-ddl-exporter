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
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Json;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class BuildSsdtPipelineStepTests
{
    [Fact]
    public async Task BootstrapStep_populates_context_and_logs_request()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var context = new BuildSsdtPipelineContext(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var step = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), new ProfileSnapshotDeserializer());

        var result = await step.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.BootstrapContext);
        Assert.NotNull(context.Profile);
        Assert.NotNull(context.FilteredModel);
        var log = context.Log.Build();
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
        var context = new BuildSsdtPipelineContext(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var step = new BuildSsdtEvidenceCacheStep(cacheService);

        var result = await step.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(cacheResult, context.EvidenceCache);
        var log = context.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "evidence.cache.requested");
        Assert.Contains(log.Entries, entry => entry.Step == "evidence.cache.persisted" || entry.Step == "evidence.cache.reused");
    }

    [Fact]
    public async Task PolicyStep_synthesizes_decision_report()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var context = new BuildSsdtPipelineContext(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), new ProfileSnapshotDeserializer());
        await bootstrapStep.ExecuteAsync(context);
        var step = new BuildSsdtPolicyDecisionStep(new TighteningPolicy());

        var result = await step.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.DecisionReport);
        Assert.True(context.DecisionReport!.ColumnCount > 0);
        var log = context.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "policy.decisions.synthesized");
    }

    [Fact]
    public async Task EmissionStep_writes_manifest_and_decision_log()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var context = new BuildSsdtPipelineContext(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), new ProfileSnapshotDeserializer());
        await bootstrapStep.ExecuteAsync(context);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy());
        await policyStep.ExecuteAsync(context);
        var step = new BuildSsdtEmissionStep(new SmoModelFactory(), new SsdtEmitter(), new PolicyDecisionLogWriter(), new EmissionFingerprintCalculator());

        var result = await step.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.Manifest);
        Assert.False(context.Manifest!.Tables.Count == 0);
        Assert.NotNull(context.DecisionLogPath);
        Assert.True(File.Exists(Path.Combine(output.Path, "manifest.json")));
        Assert.True(File.Exists(context.DecisionLogPath));
        var log = context.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.emission.completed");
        Assert.Contains(log.Entries, entry => entry.Step == "policy.log.persisted");
    }

    [Fact]
    public async Task StaticSeedStep_generates_seed_scripts()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());
        var context = new BuildSsdtPipelineContext(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(new PipelineBootstrapper(), new ProfileSnapshotDeserializer());
        await bootstrapStep.ExecuteAsync(context);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy());
        await policyStep.ExecuteAsync(context);
        var emissionStep = new BuildSsdtEmissionStep(new SmoModelFactory(), new SsdtEmitter(), new PolicyDecisionLogWriter(), new EmissionFingerprintCalculator());
        await emissionStep.ExecuteAsync(context);
        var step = new BuildSsdtStaticSeedStep(new StaticEntitySeedScriptGenerator(), StaticEntitySeedTemplate.Load());

        var result = await step.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.False(context.StaticSeedScriptPaths.IsDefaultOrEmpty);
        Assert.All(context.StaticSeedScriptPaths, path => Assert.True(File.Exists(path)));
        var log = context.Log.Build();
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
                        StaticEntityRow.Create(new object?[] { 1, "Sample" })
                    }))
                .Cast<StaticEntityTableData>()
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(tables));
        }
    }
}
