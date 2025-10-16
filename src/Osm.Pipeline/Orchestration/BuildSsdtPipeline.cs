using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.Mediation;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtPipeline : ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>
{
    private readonly IPipelineBootstrapper _bootstrapper;
    private readonly TighteningPolicy _tighteningPolicy;
    private readonly SmoModelFactory _smoModelFactory;
    private readonly SsdtEmitter _ssdtEmitter;
    private readonly PolicyDecisionLogWriter _decisionLogWriter;
    private readonly PipelineExecutionLogWriter _pipelineLogWriter;
    private readonly IEvidenceCacheService _evidenceCacheService;
    private readonly StaticEntitySeedScriptGenerator _seedGenerator;
    private readonly StaticEntitySeedTemplate _seedTemplate;
    private readonly ProfileSnapshotDeserializer _profileSnapshotDeserializer;
    private readonly EmissionFingerprintCalculator _fingerprintCalculator;
    private readonly TimeProvider _timeProvider;

    public BuildSsdtPipeline(
        IPipelineBootstrapper? bootstrapper = null,
        TighteningPolicy? tighteningPolicy = null,
        SmoModelFactory? smoModelFactory = null,
        SsdtEmitter? ssdtEmitter = null,
        PolicyDecisionLogWriter? decisionLogWriter = null,
        PipelineExecutionLogWriter? pipelineLogWriter = null,
        IEvidenceCacheService? evidenceCacheService = null,
        StaticEntitySeedScriptGenerator? seedGenerator = null,
        StaticEntitySeedTemplate? seedTemplate = null,
        ProfileSnapshotDeserializer? profileSnapshotDeserializer = null,
        EmissionFingerprintCalculator? fingerprintCalculator = null,
        TimeProvider? timeProvider = null)
    {
        _bootstrapper = bootstrapper ?? new PipelineBootstrapper();
        _tighteningPolicy = tighteningPolicy ?? new TighteningPolicy();
        _smoModelFactory = smoModelFactory ?? new SmoModelFactory();
        _ssdtEmitter = ssdtEmitter ?? new SsdtEmitter();
        _decisionLogWriter = decisionLogWriter ?? new PolicyDecisionLogWriter();
        _pipelineLogWriter = pipelineLogWriter ?? new PipelineExecutionLogWriter();
        _evidenceCacheService = evidenceCacheService ?? new EvidenceCacheService();
        _seedGenerator = seedGenerator ?? new StaticEntitySeedScriptGenerator();
        _seedTemplate = seedTemplate ?? StaticEntitySeedTemplate.Load();
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? new ProfileSnapshotDeserializer();
        _fingerprintCalculator = fingerprintCalculator ?? new EmissionFingerprintCalculator();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<BuildSsdtPipelineResult>> HandleAsync(
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

        var log = new PipelineExecutionLogBuilder(_timeProvider);
        var telemetry = new PipelineBootstrapTelemetry(
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
                ["emission.sanitizeModuleNames"] = request.SmoOptions.SanitizeModuleNames ? "true" : "false",
                ["emission.moduleParallelism"] = request.SmoOptions.ModuleParallelism.ToString(CultureInfo.InvariantCulture)
            },
            "Capturing profiling snapshot.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["provider"] = request.ProfilerProvider,
                ["profilePath"] = request.ProfilePath
            },
            "Captured profiling snapshot.");

        var bootstrapRequest = new PipelineBootstrapRequest(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            telemetry,
            (model, token) => CaptureProfileAsync(request, model, token));

        var bootstrapResult = await _bootstrapper
            .BootstrapAsync(log, bootstrapRequest, cancellationToken)
            .ConfigureAwait(false);
        if (bootstrapResult.IsFailure)
        {
            return Result<BuildSsdtPipelineResult>.Failure(bootstrapResult.Errors);
        }

        var bootstrapContext = bootstrapResult.Value;
        var filteredModel = bootstrapContext.FilteredModel;
        var supplementalEntities = bootstrapContext.SupplementalEntities;
        var profile = bootstrapContext.Profile;
        var pipelineWarnings = bootstrapContext.Warnings;

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
            var cacheEvaluation = cacheResult.Evaluation;
            var cacheMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["cacheDirectory"] = cacheResult.CacheDirectory,
                ["artifactCount"] = cacheResult.Manifest.Artifacts.Count.ToString(CultureInfo.InvariantCulture),
                ["cacheKey"] = cacheResult.Manifest.Key,
                ["cacheOutcome"] = cacheEvaluation.Outcome.ToString(),
                ["cacheReason"] = cacheEvaluation.Reason.ToString(),
            };

            foreach (var pair in cacheEvaluation.Metadata)
            {
                cacheMetadata[pair.Key] = pair.Value;
            }

            var cacheEvent = cacheEvaluation.Outcome == EvidenceCacheOutcome.Reused
                ? "evidence.cache.reused"
                : "evidence.cache.persisted";

            var cacheMessage = cacheEvaluation.Outcome == EvidenceCacheOutcome.Reused
                ? "Reused evidence cache manifest."
                : "Persisted evidence cache manifest.";

            log.Record(cacheEvent, cacheMessage, cacheMetadata);
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
            supplementalEntities,
            request.TypeMappingPolicy);
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

        var emissionMetadata = _fingerprintCalculator.Compute(smoModel, decisions, request.SmoOptions);

        var emissionSmoOptions = request.SmoOptions;
        if (emissionSmoOptions.Header.Enabled)
        {
            var headerOptions = emissionSmoOptions.Header with
            {
                Source = request.ModelPath,
                Profile = request.ProfilePath ?? request.ProfilerProvider,
                Decisions = BuildDecisionSummary(request.TighteningOptions, decisionReport),
                FingerprintAlgorithm = emissionMetadata.Algorithm,
                FingerprintHash = emissionMetadata.Hash,
                AdditionalItems = emissionSmoOptions.Header.AdditionalItems,
            };

            emissionSmoOptions = emissionSmoOptions.WithHeaderOptions(headerOptions);
        }

        var coverageResult = EmissionCoverageCalculator.Compute(
            filteredModel,
            supplementalEntities,
            decisions,
            smoModel,
            emissionSmoOptions);

        var manifest = await _ssdtEmitter.EmitAsync(
            smoModel,
            request.OutputDirectory,
            emissionSmoOptions,
            emissionMetadata,
            decisionReport,
            coverage: coverageResult.Summary,
            unsupported: coverageResult.Unsupported,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

        var seedPaths = ImmutableArray<string>.Empty;
        var seedDefinitions = StaticEntitySeedDefinitionBuilder.Build(filteredModel, emissionSmoOptions.NamingOverrides);
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

            var deterministicData = StaticEntitySeedDeterminizer.Normalize(staticDataResult.Value);
            var seedOptions = request.TighteningOptions.Emission.StaticSeeds;
            var seedsRoot = request.SeedOutputDirectoryHint;
            if (string.IsNullOrWhiteSpace(seedsRoot))
            {
                seedsRoot = Path.Combine(request.OutputDirectory, "Seeds");
            }

            Directory.CreateDirectory(seedsRoot!);
            var seedPathBuilder = ImmutableArray.CreateBuilder<string>();

            if (seedOptions.GroupByModule)
            {
                var grouped = deterministicData
                    .GroupBy(table => table.Definition.Module, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

                foreach (var group in grouped)
                {
                    var sanitizedModule = request.SmoOptions.SanitizeModuleNames
                        ? ModuleNameSanitizer.Sanitize(group.Key)
                        : group.Key;

                    var moduleDirectory = Path.Combine(seedsRoot!, sanitizedModule);
                    Directory.CreateDirectory(moduleDirectory);

                    var moduleTables = group
                        .OrderBy(table => table.Definition.LogicalName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(table => table.Definition.EffectiveName, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    var modulePath = Path.Combine(moduleDirectory, "StaticEntities.seed.sql");
                    await _seedGenerator
                        .WriteAsync(modulePath, _seedTemplate, moduleTables, seedOptions.SynchronizationMode, cancellationToken)
                        .ConfigureAwait(false);
                    seedPathBuilder.Add(modulePath);
                }
            }
            else
            {
                var seedPath = Path.Combine(seedsRoot!, "StaticEntities.seed.sql");
                await _seedGenerator
                    .WriteAsync(seedPath, _seedTemplate, deterministicData, seedOptions.SynchronizationMode, cancellationToken)
                    .ConfigureAwait(false);
                seedPathBuilder.Add(seedPath);
            }

            seedPaths = seedPathBuilder.ToImmutable();
            log.Record(
                "staticData.seed.generated",
                "Generated static entity seed scripts.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["paths"] = seedPaths.IsDefaultOrEmpty ? string.Empty : string.Join(";", seedPaths),
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
                ["seedScriptPaths"] = seedPaths.IsDefaultOrEmpty ? "<none>" : string.Join(";", seedPaths),
                ["cacheDirectory"] = cacheResult?.CacheDirectory
            });

        var executionLog = log.Build();
        var pipelineLogPath = await _pipelineLogWriter
            .WriteAsync(request.OutputDirectory, executionLog, cancellationToken)
            .ConfigureAwait(false);

        return new BuildSsdtPipelineResult(
            profile,
            decisionReport,
            manifest,
            decisionLogPath,
            pipelineLogPath,
            seedPaths,
            cacheResult,
            executionLog,
            pipelineWarnings);
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
            var profilerOptions = SqlProfilerOptions.Default with
            {
                CommandTimeoutSeconds = request.SqlOptions.CommandTimeoutSeconds,
                Sampling = sampling
            };
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

    private static string BuildDecisionSummary(TighteningOptions options, PolicyDecisionReport report)
    {
        var parts = new List<string>(7)
        {
            $"Mode={options.Policy.Mode}",
            $"NullBudget={options.Policy.NullBudget.ToString("0.###", CultureInfo.InvariantCulture)}",
            $"Columns={report.ColumnCount}",
            $"Tightened={report.TightenedColumnCount}",
            $"Unique={report.UniqueIndexCount}",
            $"FK={report.ForeignKeyCount}",
            $"FKEnabled={options.ForeignKeys.EnableCreation}",
        };

        return string.Join("; ", parts);
    }
}
