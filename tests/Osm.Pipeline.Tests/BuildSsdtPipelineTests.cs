using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Configuration;
using Osm.Validation.Tightening;
using Osm.Smo;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class BuildSsdtPipelineTests
{
    [Fact]
    public async Task HandleAsync_returns_failure_when_model_path_missing()
    {
        var scope = CreateScope(modelPath: null!);

        var request = new BuildSsdtPipelineRequest(
            scope,
            OutputDirectory: Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
            ProfilerProvider: "fixture",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);

        var pipeline = CreatePipeline();
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.buildSsdt.model.missing", error.Code);
    }

    [Fact]
    public async Task HandleAsync_returns_failure_when_output_directory_missing()
    {
        var scope = CreateScope(modelPath: Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

        var request = new BuildSsdtPipelineRequest(
            scope,
            OutputDirectory: string.Empty,
            ProfilerProvider: "fixture",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);

        var pipeline = CreatePipeline();
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pipeline.buildSsdt.output.missing");
    }

    [Fact]
    public async Task HandleAsync_uses_fixture_profile_strategy_via_bootstrapper()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        var bootstrapper = new FakePipelineBootstrapper(async (_, request, token) =>
        {
            Assert.Equal("Received build-ssdt pipeline request.", request.Telemetry.RequestMessage);
            Assert.Equal("fixture", request.Telemetry.ProfilingStartMetadata["profiling.provider"]);

            var model = LoadModel(request.ModelPath);
            var captureResult = await request.ProfileCaptureAsync(model, token);
            Assert.True(captureResult.IsSuccess);

            var error = ValidationError.Create("test.bootstrap.stop", "Bootstrapper halted pipeline for verification.");
            return Result<PipelineBootstrapContext>.Failure(error);
        });

        var scope = CreateScope(
            modelPath: modelPath,
            profilePath: profilePath);

        var request = new BuildSsdtPipelineRequest(
            scope,
            outputDirectory,
            "fixture",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);

        var pipeline = CreatePipeline(bootstrapper);
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
            Assert.Equal("sql", request.Telemetry.ProfilingStartMetadata["profiling.provider"]);

            var model = LoadModel(request.ModelPath);
            var captureResult = await request.ProfileCaptureAsync(model, token);
            Assert.True(captureResult.IsFailure);
            var captureError = Assert.Single(captureResult.Errors);
            Assert.Equal("pipeline.buildSsdt.sql.connectionString.missing", captureError.Code);

            return Result<PipelineBootstrapContext>.Failure(captureResult.Errors);
        });

        var scope = CreateScope(modelPath: modelPath);

        var request = new BuildSsdtPipelineRequest(
            scope,
            outputDirectory,
            "sql",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);

        var pipeline = CreatePipeline(bootstrapper);
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

        var scope = CreateScope(
            modelPath: modelPath,
            profilePath: profilePath);

        var request = new BuildSsdtPipelineRequest(
            scope,
            output.Path,
            "fixture",
            new EvidenceCachePipelineOptions(
                cache.Path,
                Refresh: false,
                Command: "build-ssdt",
                ModelPath: modelPath,
                ProfilePath: profilePath,
                DmmPath: null,
                ConfigPath: null,
                Metadata: new Dictionary<string, string?>()),
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            new EmptyStaticEntityDataProvider(),
            Path.Combine(output.Path, "Seeds"),
            Path.Combine(output.Path, "DynamicData"),
            SqlProjectPathHint: Path.Combine(output.Path, "OutSystemsModel.sqlproj"));

        var pipeline = CreatePipeline();
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
        Assert.False(value.TelemetryPackagePaths.IsDefaultOrEmpty);
        var packagePath = Assert.Single(value.TelemetryPackagePaths);
        Assert.True(File.Exists(packagePath));
        Assert.True(File.Exists(Path.Combine(output.Path, "policy-decisions.json")));
        Assert.NotNull(value.ExecutionLog);
        Assert.True(value.ExecutionLog.Entries.Count > 0);
        Assert.Contains(value.ExecutionLog.Entries, entry => entry.Step == "pipeline.completed");

        Assert.False(string.IsNullOrWhiteSpace(value.SqlProjectPath));
        Assert.True(File.Exists(value.SqlProjectPath));
        var projectContent = await File.ReadAllTextAsync(value.SqlProjectPath);
        Assert.Contains("Modules\\AppCore\\dbo.Customer.sql", projectContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Seeds\\**\\*.sql", projectContent, StringComparison.OrdinalIgnoreCase);

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
        Assert.Contains("ssdt.sqlproj.generated", steps);
        Assert.Contains("ssdt.sql.validation.completed", steps);
        Assert.Contains("policy.log.persisted", steps);
        Assert.Contains("staticData.seed.generated", steps);
        Assert.Contains("pipeline.execution", steps);
        Assert.Contains(steps, step => step is "evidence.cache.persisted" or "evidence.cache.reused");

        Assert.Equal(value.Manifest.Tables.Count, value.SqlValidation.TotalFiles);
        Assert.Equal(0, value.SqlValidation.ErrorCount);

        var requestIndex = Array.IndexOf(steps, "request.received");
        var completedIndex = Array.IndexOf(steps, "pipeline.completed");
        Assert.True(requestIndex >= 0 && completedIndex > requestIndex);

        var expectedWarnings = new[]
        {
            "Schema validation encountered 9 issue(s). Proceeding with best-effort import.",
            "  Example 1: /modules/0/entities/0/meta: Value is \"string\" but should be \"object\"",
            "  Example 2: /modules/0/entities/0/indexes/0/fill_factor: All values fail against the false schema",
            "  Example 3: /modules/0/entities/0/indexes/1/fill_factor: All values fail against the false schema",
            "  … 6 additional issue(s) suppressed."
        };

        Assert.Equal(expectedWarnings, value.Warnings.ToArray());

        Assert.NotNull(value.EvidenceCache);
        Assert.True(Directory.Exists(value.EvidenceCache!.CacheDirectory));
        Assert.True(File.Exists(Path.Combine(value.EvidenceCache.CacheDirectory, "manifest.json")));

        var manifestJson = await File.ReadAllTextAsync(Path.Combine(output.Path, "manifest.json"));
        using var manifestDocument = JsonDocument.Parse(manifestJson);
        Assert.True(manifestDocument.RootElement.GetProperty("Tables").GetArrayLength() > 0);

        var coverageElement = manifestDocument.RootElement.GetProperty("Coverage");
        Assert.Equal(4, coverageElement.GetProperty("Tables").GetProperty("Emitted").GetInt32());
        Assert.Equal(5, coverageElement.GetProperty("Tables").GetProperty("Total").GetInt32());
        Assert.Equal(80.0, coverageElement.GetProperty("Tables").GetProperty("Percentage").GetDouble(), precision: 2);

        Assert.Equal(14, coverageElement.GetProperty("Columns").GetProperty("Emitted").GetInt32());
        Assert.Equal(17, coverageElement.GetProperty("Columns").GetProperty("Total").GetInt32());
        Assert.Equal(82.35, coverageElement.GetProperty("Columns").GetProperty("Percentage").GetDouble(), precision: 2);

        Assert.Equal(8, coverageElement.GetProperty("Constraints").GetProperty("Emitted").GetInt32());
        Assert.Equal(9, coverageElement.GetProperty("Constraints").GetProperty("Total").GetInt32());
        Assert.Equal(88.89, coverageElement.GetProperty("Constraints").GetProperty("Percentage").GetDouble(), precision: 2);

        Assert.Equal(JsonValueKind.Array, manifestDocument.RootElement.GetProperty("Unsupported").ValueKind);
    }

    [Fact]
    public async Task HandleAsync_returns_failure_when_sql_validation_reports_errors()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();

        var scope = CreateScope(
            modelPath: modelPath,
            profilePath: profilePath);

        var request = new BuildSsdtPipelineRequest(
            scope,
            output.Path,
            "fixture",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);

        var issue = SsdtSqlValidationIssue.Create(
            "Modules/Sample/dbo.Entity.sql",
            new[]
            {
                SsdtSqlValidationError.Create(102, 0, 16, 1, 1, "Incorrect syntax near '?'."),
            });
        var summary = SsdtSqlValidationSummary.Create(1, new[] { issue });
        var pipeline = CreatePipeline(sqlValidator: new FakeSqlValidator(summary));

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pipeline.buildSsdt.sql.validationFailed");
    }

    private static ModelExecutionScope CreateScope(
        string? modelPath = null,
        ModuleFilterOptions? moduleFilter = null,
        SupplementalModelOptions? supplemental = null,
        TighteningOptions? tightening = null,
        ResolvedSqlOptions? sqlOptions = null,
        SmoBuildOptions? smoOptions = null,
        TypeMappingPolicy? typeMappingPolicy = null,
        string? profilePath = null)
    {
        var resolvedTightening = tightening ?? TighteningOptions.Default;
        return new ModelExecutionScope(
            modelPath ?? FixtureFile.GetPath("model.edge-case.json"),
            moduleFilter ?? ModuleFilterOptions.IncludeAll,
            supplemental ?? SupplementalModelOptions.Default,
            resolvedTightening,
            sqlOptions ?? new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            smoOptions ?? SmoBuildOptions.FromEmission(resolvedTightening.Emission),
            typeMappingPolicy ?? TypeMappingPolicyLoader.LoadDefault(),
            profilePath);
    }

    private static BuildSsdtPipeline CreatePipeline(
        IPipelineBootstrapper? bootstrapper = null,
        ISsdtSqlValidator? sqlValidator = null)
    {
        var timeProvider = TimeProvider.System;
        var bootstrapStep = new BuildSsdtBootstrapStep(
            bootstrapper ?? CreatePipelineBootstrapper(),
            CreateProfilerFactory());
        var evidenceCacheStep = new BuildSsdtEvidenceCacheStep(new EvidenceCacheCoordinator(new EvidenceCacheService()));
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var validationStep = new BuildSsdtSqlValidationStep(sqlValidator ?? new SsdtSqlValidator());
        var staticSeedStep = new BuildSsdtStaticSeedStep(CreateSeedGenerator());
        var dynamicInsertStep = new BuildSsdtDynamicInsertStep(new DynamicEntityInsertGenerator(new SqlLiteralFormatter()));
        var telemetryPackagingStep = new BuildSsdtTelemetryPackagingStep();

        return new BuildSsdtPipeline(
            timeProvider,
            bootstrapStep,
            evidenceCacheStep,
            policyStep,
            emissionStep,
            sqlProjectStep,
            validationStep,
            staticSeedStep,
            dynamicInsertStep,
            telemetryPackagingStep);
    }

    private static PipelineBootstrapper CreatePipelineBootstrapper()
    {
        return new PipelineBootstrapper(
            new ModelIngestionService(new ModelJsonDeserializer()),
            new ModuleFilter(),
            new SupplementalEntityLoader(new ModelJsonDeserializer()),
            new ProfilingInsightGenerator());
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

    private sealed class FakeSqlValidator : ISsdtSqlValidator
    {
        private readonly SsdtSqlValidationSummary _summary;

        public FakeSqlValidator(SsdtSqlValidationSummary summary)
        {
            _summary = summary;
        }

        public Task<SsdtSqlValidationSummary> ValidateAsync(
            string outputDirectory,
            IReadOnlyList<TableManifestEntry> tables,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_summary);
        }
    }

    private static OsmModel LoadModel(string modelPath)
    {
        using var stream = File.OpenRead(modelPath);
        var warnings = new List<string>();
        var deserializer = new ModelJsonDeserializer();
        var result = deserializer.Deserialize(stream, warnings);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
