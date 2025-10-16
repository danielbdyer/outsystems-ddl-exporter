using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Osm.Smo;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class BuildSsdtPipelineTests
{
    [Fact]
    public async Task HandleAsync_uses_fixture_profile_strategy_via_bootstrapper()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        var bootstrapper = new FakePipelineBootstrapper(async (_, request, token) =>
        {
            Assert.Equal("Received build-ssdt pipeline request.", request.Telemetry.RequestMessage);
            Assert.Equal("fixture", request.Telemetry.ProfilingStartMetadata["provider"]);

            var captureResult = await request.ProfileCaptureAsync(default!, token);
            Assert.True(captureResult.IsSuccess);

            var error = ValidationError.Create("test.bootstrap.stop", "Bootstrapper halted pipeline for verification.");
            return Result<PipelineBootstrapContext>.Failure(error);
        });

        var request = new BuildSsdtPipelineRequest(
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
            null,
            null,
            null);

        var pipeline = new BuildSsdtPipeline(bootstrapper: bootstrapper);
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("test.bootstrap.stop", error.Code);
        Assert.NotNull(bootstrapper.LastRequest);
    }

    [Fact]
    public async Task HandleAsync_uses_sql_profile_strategy_via_bootstrapper()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        var bootstrapper = new FakePipelineBootstrapper(async (_, request, token) =>
        {
            Assert.Equal("sql", request.Telemetry.ProfilingStartMetadata["provider"]);

            var captureResult = await request.ProfileCaptureAsync(default!, token);
            Assert.True(captureResult.IsFailure);
            var captureError = Assert.Single(captureResult.Errors);
            Assert.Equal("pipeline.buildSsdt.sql.connectionString.missing", captureError.Code);

            return Result<PipelineBootstrapContext>.Failure(captureResult.Errors);
        });

        var request = new BuildSsdtPipelineRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            outputDirectory,
            TighteningOptions.Default,
            SupplementalModelOptions.Default,
            "sql",
            null,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null)),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicy.LoadDefault(),
            null,
            null,
            null);

        var pipeline = new BuildSsdtPipeline(bootstrapper: bootstrapper);
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pipeline.buildSsdt.sql.connectionString.missing");
        Assert.NotNull(bootstrapper.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsync_emits_manifest_seed_and_cache()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();
        using var cache = new TempDirectory();

        var request = new BuildSsdtPipelineRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            output.Path,
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
            new EvidenceCachePipelineOptions(
                cache.Path,
                Refresh: false,
                Command: "build-ssdt",
                ModelPath: modelPath,
                ProfilePath: profilePath,
                DmmPath: null,
                ConfigPath: null,
                Metadata: new Dictionary<string, string?>()),
            new EmptyStaticEntityDataProvider(),
            Path.Combine(output.Path, "Seeds"));

        var pipeline = new BuildSsdtPipeline();
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        var value = result.Value;

        Assert.NotNull(value.Manifest);
        Assert.True(File.Exists(Path.Combine(output.Path, "manifest.json")));
        Assert.True(File.Exists(value.DecisionLogPath));
        Assert.False(value.StaticSeedScriptPaths.IsDefaultOrEmpty);
        Assert.NotEmpty(value.StaticSeedScriptPaths);
        foreach (var path in value.StaticSeedScriptPaths)
        {
            Assert.True(File.Exists(path));
        }
        Assert.True(File.Exists(Path.Combine(output.Path, "policy-decisions.json")));
        Assert.NotNull(value.ExecutionLog);
        Assert.True(value.ExecutionLog.Entries.Count > 0);
        Assert.Contains(value.ExecutionLog.Entries, entry => entry.Step == "pipeline.completed");

        var steps = value.ExecutionLog.Entries.Select(entry => entry.Step).ToArray();
        Assert.Contains("request.received", steps);
        Assert.Contains("model.ingested", steps);
        Assert.Contains("model.filtered", steps);
        Assert.Contains("supplemental.loaded", steps);
        Assert.Contains("profiling.capture.start", steps);
        Assert.Contains("profiling.capture.completed", steps);
        Assert.Contains("policy.decisions.synthesized", steps);
        Assert.Contains("smo.model.created", steps);
        Assert.Contains("ssdt.emission.completed", steps);
        Assert.Contains("policy.log.persisted", steps);
        Assert.Contains("staticData.seed.generated", steps);
        Assert.Contains(steps, step => step is "evidence.cache.persisted" or "evidence.cache.reused");

        var requestIndex = Array.IndexOf(steps, "request.received");
        var completedIndex = Array.IndexOf(steps, "pipeline.completed");
        Assert.True(requestIndex >= 0 && completedIndex > requestIndex);

        Assert.True(value.Warnings.IsDefaultOrEmpty);

        Assert.NotNull(value.EvidenceCache);
        Assert.True(Directory.Exists(value.EvidenceCache!.CacheDirectory));
        Assert.True(File.Exists(Path.Combine(value.EvidenceCache.CacheDirectory, "manifest.json")));

        var manifestJson = await File.ReadAllTextAsync(Path.Combine(output.Path, "manifest.json"));
        using var manifestDocument = JsonDocument.Parse(manifestJson);
        Assert.True(manifestDocument.RootElement.GetProperty("Tables").GetArrayLength() > 0);
        var coverageElement = manifestDocument.RootElement.GetProperty("Coverage");
        Assert.True(coverageElement.GetProperty("Tables").GetProperty("Total").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Array, manifestDocument.RootElement.GetProperty("Unsupported").ValueKind);
    }

    private sealed class FakePipelineBootstrapper : IPipelineBootstrapper
    {
        private readonly Func<PipelineExecutionLogBuilder, PipelineBootstrapRequest, CancellationToken, Task<Result<PipelineBootstrapContext>>> _callback;

        public FakePipelineBootstrapper(
            Func<PipelineExecutionLogBuilder, PipelineBootstrapRequest, CancellationToken, Task<Result<PipelineBootstrapContext>>> callback)
        {
            _callback = callback;
        }

        public PipelineBootstrapRequest? LastRequest { get; private set; }

        public Task<Result<PipelineBootstrapContext>> BootstrapAsync(
            PipelineExecutionLogBuilder log,
            PipelineBootstrapRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return _callback(log, request, cancellationToken);
        }
    }

    private sealed class EmptyStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            var data = definitions
                .Select(definition => StaticEntityTableData.Create(definition, Enumerable.Empty<StaticEntityRow>()))
                .ToArray();
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success((IReadOnlyList<StaticEntityTableData>)data));
        }
    }
}
