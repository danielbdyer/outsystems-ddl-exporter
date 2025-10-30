using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Domain.Profiling;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class CaptureProfilePipelineTests
{
    [Fact]
    public async Task HandleAsync_returns_failure_when_model_missing()
    {
        var pipeline = CreatePipeline();
        var request = CreateRequest(modelPath: string.Empty);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pipeline.captureProfile.model.missing");
    }

    [Fact]
    public async Task HandleAsync_returns_failure_when_output_missing()
    {
        var pipeline = CreatePipeline();
        var request = CreateRequest(outputDirectory: string.Empty);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pipeline.captureProfile.output.missing");
    }

    [Fact]
    public async Task HandleAsync_captures_fixture_profile_and_writes_manifest()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var fixtureProfile = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();
        var pipeline = CreatePipeline();
        var request = CreateRequest(modelPath: modelPath, fixtureProfilePath: fixtureProfile, outputDirectory: output.Path);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        var payload = result.Value;

        Assert.Equal(Path.Combine(output.Path, "profile.json"), payload.ProfilePath);
        Assert.Equal(Path.Combine(output.Path, "manifest.json"), payload.ManifestPath);

        Assert.True(File.Exists(payload.ProfilePath));
        Assert.True(File.Exists(payload.ManifestPath));

        using (var stream = File.OpenRead(payload.ProfilePath))
        {
            var deserializer = new ProfileSnapshotDeserializer();
            var snapshotResult = deserializer.Deserialize(stream);
            Assert.True(snapshotResult.IsSuccess);
            Assert.Equal(snapshotResult.Value.Columns.Length, payload.Profile.Columns.Length);
        }

        using (var manifestStream = File.OpenRead(payload.ManifestPath))
        {
            using var document = JsonDocument.Parse(manifestStream);
            var root = document.RootElement;

            Assert.Equal(modelPath, root.GetProperty("modelPath").GetString());
            Assert.Equal(payload.ProfilePath, root.GetProperty("profilePath").GetString());
            Assert.Equal("fixture", root.GetProperty("profilerProvider").GetString());

            var snapshot = root.GetProperty("snapshot");
            Assert.Equal(payload.Profile.Columns.Length, snapshot.GetProperty("columnCount").GetInt32());
            Assert.Equal(payload.Profile.ForeignKeys.Length, snapshot.GetProperty("foreignKeyCount").GetInt32());
        }

        Assert.NotEmpty(payload.ExecutionLog.Entries);
        Assert.Contains(payload.ExecutionLog.Entries, entry => entry.Step == "profiling.persisted");
    }

    [Fact]
    public async Task HandleAsync_PropagatesCoverageAnomaliesToManifest()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var coordinate = ProfilingInsightCoordinate.Create(
            model.Modules[0].Entities[0].Schema,
            model.Modules[0].Entities[0].PhysicalName,
            model.Modules[0].Entities[0].Attributes[0].ColumnName).Value;
        var anomaly = ProfilingCoverageAnomaly.Create(
            ProfilingCoverageAnomalyType.NullCountProbeMissing,
            "Null-count probe missing",
            "Increase timeout",
            coordinate,
            new[] { model.Modules[0].Entities[0].Attributes[0].ColumnName.Value },
            ProfilingProbeOutcome.Unknown);

        var profile = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>(),
            new[] { anomaly }).Value;

        var context = new PipelineBootstrapContext(
            model,
            ImmutableArray<EntityModel>.Empty,
            profile,
            ImmutableArray<ProfilingInsight>.Empty,
            ImmutableArray<string>.Empty);

        using var output = new TempDirectory();
        var timeProvider = TimeProvider.System;
        var serializer = new ProfileSnapshotSerializer();
        var pipeline = new CaptureProfilePipeline(
            timeProvider,
            new StubBootstrapper(context),
            new NoopProfilerFactory(),
            serializer);

        var request = CreateRequest(outputDirectory: output.Path);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        var payload = result.Value;

        Assert.Contains(payload.CoverageAnomalies, entry => entry.Type == ProfilingCoverageAnomalyType.NullCountProbeMissing);
        Assert.Contains(payload.Manifest.Warnings, warning => warning.Contains("Coverage anomaly", StringComparison.Ordinal));
        Assert.Single(payload.Manifest.CoverageAnomalies);
        Assert.Equal("Coverage", payload.Manifest.Insights.Last().Category);
    }

    private static CaptureProfilePipeline CreatePipeline()
    {
        var timeProvider = TimeProvider.System;
        var deserializer = new ModelJsonDeserializer();
        var modelIngestion = new ModelIngestionService(deserializer);
        var bootstrapper = new PipelineBootstrapper(
            modelIngestion,
            new ModuleFilter(),
            new SupplementalEntityLoader(),
            new ProfilingInsightGenerator());

        var profileDeserializer = new ProfileSnapshotDeserializer();
        var profilerFactory = new DataProfilerFactory(profileDeserializer, (_, _) => new FakeConnectionFactory());
        var serializer = new ProfileSnapshotSerializer();

        return new CaptureProfilePipeline(timeProvider, bootstrapper, profilerFactory, serializer);
    }

    private static CaptureProfilePipelineRequest CreateRequest(
        string? modelPath = null,
        string? fixtureProfilePath = null,
        string? outputDirectory = null)
    {
        modelPath ??= FixtureFile.GetPath("model.edge-case.json");
        fixtureProfilePath ??= FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        outputDirectory ??= Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

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
            fixtureProfilePath);

        return new CaptureProfilePipelineRequest(
            scope,
            "fixture",
            outputDirectory,
            fixtureProfilePath,
            SqlMetadataLog: null);
    }

    private sealed class FakeConnectionFactory : IDbConnectionFactory
    {
        public Task<System.Data.Common.DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<System.Data.Common.DbConnection>(
                new InvalidOperationException("SQL profiler should not be invoked in fixture-driven tests."));
        }
    }

    private sealed class StubBootstrapper : IPipelineBootstrapper
    {
        private readonly PipelineBootstrapContext _context;

        public StubBootstrapper(PipelineBootstrapContext context)
        {
            _context = context;
        }

        public Task<Result<PipelineBootstrapContext>> BootstrapAsync(
            PipelineExecutionLogBuilder log,
            PipelineBootstrapRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<PipelineBootstrapContext>.Success(_context));
        }
    }

    private sealed class NoopProfilerFactory : IDataProfilerFactory
    {
        public Result<IDataProfiler> Create(BuildSsdtPipelineRequest request, OsmModel model)
        {
            throw new InvalidOperationException("Profiler factory should not be invoked in coverage anomaly tests.");
        }
    }
}
