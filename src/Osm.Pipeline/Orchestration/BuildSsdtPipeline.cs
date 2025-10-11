using System;
using System.Collections.Generic;
using System.IO;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPipeline
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ModuleFilter _moduleFilter;
    private readonly SupplementalEntityLoader _supplementalLoader;
    private readonly TighteningPolicy _tighteningPolicy;
    private readonly SmoModelFactory _smoModelFactory;
    private readonly SsdtEmitter _ssdtEmitter;
    private readonly PolicyDecisionLogWriter _decisionLogWriter;
    private readonly IEvidenceCacheService _evidenceCacheService;
    private readonly StaticEntitySeedScriptGenerator _seedGenerator;
    private readonly StaticEntitySeedTemplate _seedTemplate;
    private readonly ProfileSnapshotDeserializer _profileSnapshotDeserializer;

    public BuildSsdtPipeline(
        IModelIngestionService? modelIngestionService = null,
        ModuleFilter? moduleFilter = null,
        SupplementalEntityLoader? supplementalLoader = null,
        TighteningPolicy? tighteningPolicy = null,
        SmoModelFactory? smoModelFactory = null,
        SsdtEmitter? ssdtEmitter = null,
        PolicyDecisionLogWriter? decisionLogWriter = null,
        IEvidenceCacheService? evidenceCacheService = null,
        StaticEntitySeedScriptGenerator? seedGenerator = null,
        StaticEntitySeedTemplate? seedTemplate = null,
        ProfileSnapshotDeserializer? profileSnapshotDeserializer = null)
    {
        _modelIngestionService = modelIngestionService ?? new ModelIngestionService(new ModelJsonDeserializer());
        _moduleFilter = moduleFilter ?? new ModuleFilter();
        _supplementalLoader = supplementalLoader ?? new SupplementalEntityLoader();
        _tighteningPolicy = tighteningPolicy ?? new TighteningPolicy();
        _smoModelFactory = smoModelFactory ?? new SmoModelFactory();
        _ssdtEmitter = ssdtEmitter ?? new SsdtEmitter();
        _decisionLogWriter = decisionLogWriter ?? new PolicyDecisionLogWriter();
        _evidenceCacheService = evidenceCacheService ?? new EvidenceCacheService();
        _seedGenerator = seedGenerator ?? new StaticEntitySeedScriptGenerator();
        _seedTemplate = seedTemplate ?? StaticEntitySeedTemplate.Load();
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? new ProfileSnapshotDeserializer();
    }

    public async Task<Result<BuildSsdtPipelineResult>> ExecuteAsync(
        BuildSsdtPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ModelPath))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.model.missing",
                "Model path must be provided for SSDT emission.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.output.missing",
                "Output directory must be provided for SSDT emission.");
        }

        var modelResult = await _modelIngestionService.LoadFromFileAsync(request.ModelPath, cancellationToken).ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(modelResult.Errors);
        }

        var filteredResult = _moduleFilter.Apply(modelResult.Value, request.ModuleFilter);
        if (filteredResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(filteredResult.Errors);
        }

        var supplementalResult = await _supplementalLoader.LoadAsync(request.SupplementalModels, cancellationToken).ConfigureAwait(false);
        if (supplementalResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(supplementalResult.Errors);
        }

        var profileResult = await CaptureProfileAsync(request, filteredResult.Value, cancellationToken).ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;

        EvidenceCacheResult? cacheResult = null;
        if (request.EvidenceCache is { } cacheOptions && !string.IsNullOrWhiteSpace(cacheOptions.RootDirectory))
        {
            var metadata = cacheOptions.Metadata ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            var cacheRequest = new EvidenceCacheRequest(
                cacheOptions.RootDirectory!.Trim(),
                cacheOptions.Command,
                cacheOptions.ModelPath,
                cacheOptions.ProfilePath,
                cacheOptions.DmmPath,
                cacheOptions.ConfigPath,
                metadata,
                cacheOptions.Refresh);

            var cacheExecution = await _evidenceCacheService.CacheAsync(cacheRequest, cancellationToken).ConfigureAwait(false);
            if (cacheExecution.IsFailure)
            {
                return Result<BuildSsdtPipelineResult>.Failure(cacheExecution.Errors);
            }

            cacheResult = cacheExecution.Value;
        }

        var decisions = _tighteningPolicy.Decide(filteredResult.Value, profile, request.TighteningOptions);
        var decisionReport = PolicyDecisionReporter.Create(decisions);

        var smoModel = _smoModelFactory.Create(
            filteredResult.Value,
            decisions,
            profile,
            request.SmoOptions,
            supplementalResult.Value);

        var manifest = await _ssdtEmitter.EmitAsync(
            smoModel,
            request.OutputDirectory,
            request.SmoOptions,
            decisionReport,
            cancellationToken).ConfigureAwait(false);

        var decisionLogPath = await _decisionLogWriter.WriteAsync(
            request.OutputDirectory,
            decisionReport,
            cancellationToken).ConfigureAwait(false);

        string? seedPath = null;
        var seedDefinitions = StaticEntitySeedDefinitionBuilder.Build(filteredResult.Value, request.SmoOptions.NamingOverrides);
        if (!seedDefinitions.IsDefaultOrEmpty)
        {
            if (request.StaticDataProvider is null)
            {
                return ValidationError.Create(
                    "pipeline.buildSsdt.staticData.missingProvider",
                    "Static entity data provider is required when the model includes static entities.");
            }

            var staticDataResult = await request.StaticDataProvider
                .GetDataAsync(seedDefinitions, cancellationToken)
                .ConfigureAwait(false);
            if (staticDataResult.IsFailure)
            {
                return Result<BuildSsdtPipelineResult>.Failure(staticDataResult.Errors);
            }

            var targetPath = request.SeedScriptPathHint;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                var seedsRoot = Path.Combine(request.OutputDirectory, "Seeds");
                Directory.CreateDirectory(seedsRoot);
                targetPath = Path.Combine(seedsRoot, "StaticEntities.seed.sql");
            }

            await _seedGenerator.WriteAsync(targetPath!, _seedTemplate, staticDataResult.Value, cancellationToken).ConfigureAwait(false);
            seedPath = targetPath;
        }

        return new BuildSsdtPipelineResult(profile, decisionReport, manifest, decisionLogPath, seedPath, cacheResult);
    }

    private async Task<Result<ProfileSnapshot>> CaptureProfileAsync(
        BuildSsdtPipelineRequest request,
        OsmModel model,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.ProfilerProvider, "sql", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.SqlOptions.ConnectionString))
            {
                return ValidationError.Create(
                    "pipeline.buildSsdt.sql.connectionString.missing",
                    "Connection string is required when using the SQL profiler.");
            }

            var sampling = CreateSamplingOptions(request.SqlOptions.Sampling);
            var connectionOptions = CreateConnectionOptions(request.SqlOptions.Authentication);
            var profilerOptions = new SqlProfilerOptions(request.SqlOptions.CommandTimeoutSeconds, sampling);
            var sqlProfiler = new SqlDataProfiler(new SqlConnectionFactory(request.SqlOptions.ConnectionString!, connectionOptions), model, profilerOptions);
            return await sqlProfiler.CaptureAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.ProfilePath))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.profile.path.missing",
                "Profile path is required when using the fixture profiler.");
        }

        var fixtureProfiler = new FixtureDataProfiler(request.ProfilePath!, _profileSnapshotDeserializer);
        return await fixtureProfiler.CaptureAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SqlSamplingOptions CreateSamplingOptions(SqlSamplingSettings configuration)
    {
        var threshold = configuration.RowSamplingThreshold ?? SqlSamplingOptions.Default.RowCountSamplingThreshold;
        var sampleSize = configuration.SampleSize ?? SqlSamplingOptions.Default.SampleSize;
        return new SqlSamplingOptions(threshold, sampleSize);
    }

    private static SqlConnectionOptions CreateConnectionOptions(SqlAuthenticationSettings configuration)
    {
        return new SqlConnectionOptions(
            configuration.Method,
            configuration.TrustServerCertificate,
            configuration.ApplicationName,
            configuration.AccessToken);
    }
}
