using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        var log = new PipelineExecutionLogBuilder();
        log.Record(
            "request.received",
            "Received build-ssdt pipeline request.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["modelPath"] = request.ModelPath,
                ["outputDirectory"] = request.OutputDirectory,
                ["profilerProvider"] = request.ProfilerProvider,
                ["moduleFilter.hasFilter"] = request.ModuleFilter.HasFilter ? "true" : "false",
                ["moduleFilter.moduleCount"] = request.ModuleFilter.Modules.Length.ToString(CultureInfo.InvariantCulture),
                ["supplemental.includeUsers"] = request.SupplementalModels.IncludeUsers ? "true" : "false",
                ["supplemental.pathCount"] = request.SupplementalModels.Paths.Count.ToString(CultureInfo.InvariantCulture),
                ["tightening.mode"] = request.TighteningOptions.Policy.Mode.ToString(),
                ["tightening.nullBudget"] = request.TighteningOptions.Policy.NullBudget.ToString(CultureInfo.InvariantCulture),
                ["emission.includePlatformAutoIndexes"] = request.SmoOptions.IncludePlatformAutoIndexes ? "true" : "false",
                ["emission.emitBareTableOnly"] = request.SmoOptions.EmitBareTableOnly ? "true" : "false",
                ["emission.sanitizeModuleNames"] = request.SmoOptions.SanitizeModuleNames ? "true" : "false"
            });

        var modelResult = await _modelIngestionService.LoadFromFileAsync(request.ModelPath, cancellationToken).ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(modelResult.Errors);
        }

        var model = modelResult.Value;
        var moduleCount = model.Modules.Length;
        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(entity => entity.Attributes.Length));
        log.Record(
            "model.ingested",
            "Loaded OutSystems model from disk.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["modules"] = moduleCount.ToString(CultureInfo.InvariantCulture),
                ["entities"] = entityCount.ToString(CultureInfo.InvariantCulture),
                ["attributes"] = attributeCount.ToString(CultureInfo.InvariantCulture),
                ["exportedAtUtc"] = model.ExportedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            });

        var filteredResult = _moduleFilter.Apply(model, request.ModuleFilter);
        if (filteredResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(filteredResult.Errors);
        }

        var filteredModel = filteredResult.Value;
        log.Record(
            "model.filtered",
            "Applied module filter options.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["originalModules"] = moduleCount.ToString(CultureInfo.InvariantCulture),
                ["filteredModules"] = filteredModel.Modules.Length.ToString(CultureInfo.InvariantCulture),
                ["filter.includeSystemModules"] = request.ModuleFilter.IncludeSystemModules ? "true" : "false",
                ["filter.includeInactiveModules"] = request.ModuleFilter.IncludeInactiveModules ? "true" : "false"
            });

        var supplementalResult = await _supplementalLoader.LoadAsync(request.SupplementalModels, cancellationToken).ConfigureAwait(false);
        if (supplementalResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(supplementalResult.Errors);
        }

        log.Record(
            "supplemental.loaded",
            "Loaded supplemental entity definitions.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["supplementalEntityCount"] = supplementalResult.Value.Length.ToString(CultureInfo.InvariantCulture),
                ["requestedPaths"] = request.SupplementalModels.Paths.Count.ToString(CultureInfo.InvariantCulture)
            });

        log.Record(
            "profiling.capture.start",
            "Capturing profiling snapshot.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["provider"] = request.ProfilerProvider,
                ["profilePath"] = request.ProfilePath
            });

        var profileResult = await CaptureProfileAsync(request, filteredResult.Value, cancellationToken).ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;
        log.Record(
            "profiling.capture.completed",
            "Captured profiling snapshot.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["provider"] = request.ProfilerProvider,
                ["columnProfiles"] = profile.Columns.Length.ToString(CultureInfo.InvariantCulture),
                ["uniqueCandidates"] = profile.UniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
                ["compositeUniqueCandidates"] = profile.CompositeUniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = profile.ForeignKeys.Length.ToString(CultureInfo.InvariantCulture)
            });

        EvidenceCacheResult? cacheResult = null;
        if (request.EvidenceCache is { } cacheOptions && !string.IsNullOrWhiteSpace(cacheOptions.RootDirectory))
        {
            var metadata = cacheOptions.Metadata ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            log.Record(
                "evidence.cache.requested",
                "Caching pipeline inputs.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["rootDirectory"] = cacheOptions.RootDirectory?.Trim(),
                    ["refresh"] = cacheOptions.Refresh ? "true" : "false",
                    ["metadataCount"] = metadata.Count.ToString(CultureInfo.InvariantCulture)
                });
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
            log.Record(
                "evidence.cache.persisted",
                "Persisted evidence cache manifest.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["cacheDirectory"] = cacheResult.CacheDirectory,
                    ["artifactCount"] = cacheResult.Manifest.Artifacts.Count.ToString(CultureInfo.InvariantCulture),
                    ["cacheKey"] = cacheResult.Manifest.Key
                });
        }
        else
        {
            log.Record(
                "evidence.cache.skipped",
                "Evidence cache disabled for request.");
        }

        var decisions = _tighteningPolicy.Decide(filteredModel, profile, request.TighteningOptions);
        var decisionReport = PolicyDecisionReporter.Create(decisions);
        log.Record(
            "policy.decisions.synthesized",
            "Synthesized tightening decisions.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["columns"] = decisionReport.ColumnCount.ToString(CultureInfo.InvariantCulture),
                ["tightenedColumns"] = decisionReport.TightenedColumnCount.ToString(CultureInfo.InvariantCulture),
                ["remediationColumns"] = decisionReport.RemediationColumnCount.ToString(CultureInfo.InvariantCulture),
                ["uniqueIndexes"] = decisionReport.UniqueIndexCount.ToString(CultureInfo.InvariantCulture),
                ["uniqueIndexesEnforced"] = decisionReport.UniqueIndexesEnforcedCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = decisionReport.ForeignKeyCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeysCreated"] = decisionReport.ForeignKeysCreatedCount.ToString(CultureInfo.InvariantCulture)
            });

        var smoModel = _smoModelFactory.Create(
            filteredModel,
            decisions,
            profile,
            request.SmoOptions,
            supplementalResult.Value);
        var smoTableCount = smoModel.Tables.Length;
        var smoColumnCount = smoModel.Tables.Sum(static table => table.Columns.Length);
        var smoIndexCount = smoModel.Tables.Sum(static table => table.Indexes.Length);
        var smoForeignKeyCount = smoModel.Tables.Sum(static table => table.ForeignKeys.Length);
        log.Record(
            "smo.model.created",
            "Materialized SMO table graph.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tables"] = smoTableCount.ToString(CultureInfo.InvariantCulture),
                ["columns"] = smoColumnCount.ToString(CultureInfo.InvariantCulture),
                ["indexes"] = smoIndexCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = smoForeignKeyCount.ToString(CultureInfo.InvariantCulture)
            });

        var manifest = await _ssdtEmitter.EmitAsync(
            smoModel,
            request.OutputDirectory,
            request.SmoOptions,
            decisionReport,
            cancellationToken).ConfigureAwait(false);
        log.Record(
            "ssdt.emission.completed",
            "Emitted SSDT artifacts.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["outputDirectory"] = request.OutputDirectory,
                ["tableArtifacts"] = manifest.Tables.Count.ToString(CultureInfo.InvariantCulture),
                ["includesPolicySummary"] = (manifest.PolicySummary is not null) ? "true" : "false"
            });

        var decisionLogPath = await _decisionLogWriter.WriteAsync(
            request.OutputDirectory,
            decisionReport,
            cancellationToken).ConfigureAwait(false);
        log.Record(
            "policy.log.persisted",
            "Persisted policy decision log.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["path"] = decisionLogPath,
                ["diagnostics"] = decisionReport.Diagnostics.Length.ToString(CultureInfo.InvariantCulture)
            });

        string? seedPath = null;
        var seedDefinitions = StaticEntitySeedDefinitionBuilder.Build(filteredModel, request.SmoOptions.NamingOverrides);
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
            log.Record(
                "staticData.seed.generated",
                "Generated static entity seed script.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["path"] = seedPath,
                    ["tableCount"] = seedDefinitions.Length.ToString(CultureInfo.InvariantCulture)
                });
        }
        else
        {
            log.Record(
                "staticData.seed.skipped",
                "No static entity seeds required for request.");
        }

        log.Record(
            "pipeline.completed",
            "Build-SSDT pipeline completed successfully.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["manifestPath"] = Path.Combine(request.OutputDirectory, "manifest.json"),
                ["decisionLogPath"] = decisionLogPath,
                ["seedScriptPath"] = seedPath ?? "<none>",
                ["cacheDirectory"] = cacheResult?.CacheDirectory
            });

        return new BuildSsdtPipelineResult(profile, decisionReport, manifest, decisionLogPath, seedPath, cacheResult, log.Build());
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
