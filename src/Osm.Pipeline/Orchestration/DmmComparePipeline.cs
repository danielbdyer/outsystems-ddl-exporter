using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Dmm;
using Osm.Json;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Profiling;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Pipeline.Mediation;

namespace Osm.Pipeline.Orchestration;

public sealed class DmmComparePipeline : ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>
{
    private readonly IPipelineBootstrapper _bootstrapper;
    private readonly TighteningPolicy _tighteningPolicy;
    private readonly SmoModelFactory _smoModelFactory;
    private readonly IDmmLens<TextReader> _dmmScriptLens;
    private readonly IDmmLens<SmoDmmLensRequest> _smoLens;
    private readonly IDmmLens<string> _ssdtLens;
    private readonly DmmComparator _dmmComparator;
    private readonly SsdtTableLayoutComparator _ssdtLayoutComparator;
    private readonly DmmDiffLogWriter _diffLogWriter;
    private readonly EvidenceCacheCoordinator _evidenceCacheCoordinator;
    private readonly ProfileSnapshotDeserializer _profileSnapshotDeserializer;
    private readonly TimeProvider _timeProvider;

    public DmmComparePipeline(
        IPipelineBootstrapper bootstrapper,
        TighteningPolicy? tighteningPolicy = null,
        SmoModelFactory? smoModelFactory = null,
        IDmmLens<TextReader>? dmmScriptLens = null,
        IDmmLens<SmoDmmLensRequest>? smoLens = null,
        IDmmLens<string>? ssdtLens = null,
        DmmComparator? dmmComparator = null,
        SsdtTableLayoutComparator? ssdtLayoutComparator = null,
        DmmDiffLogWriter? diffLogWriter = null,
        IEvidenceCacheService? evidenceCacheService = null,
        ProfileSnapshotDeserializer? profileSnapshotDeserializer = null,
        TimeProvider? timeProvider = null)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _tighteningPolicy = tighteningPolicy ?? new TighteningPolicy();
        _smoModelFactory = smoModelFactory ?? new SmoModelFactory();
        _dmmScriptLens = dmmScriptLens ?? new ScriptDomDmmLens();
        _smoLens = smoLens ?? new SmoDmmLens();
        _ssdtLens = ssdtLens ?? new SsdtProjectDmmLens();
        _dmmComparator = dmmComparator ?? new DmmComparator();
        _ssdtLayoutComparator = ssdtLayoutComparator ?? new SsdtTableLayoutComparator();
        _diffLogWriter = diffLogWriter ?? new DmmDiffLogWriter();
        var resolvedEvidenceCacheService = evidenceCacheService ?? new EvidenceCacheService();
        _evidenceCacheCoordinator = new EvidenceCacheCoordinator(resolvedEvidenceCacheService);
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? new ProfileSnapshotDeserializer();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<DmmComparePipelineResult>> HandleAsync(
        DmmComparePipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Scope.ModelPath))
        {
            return ValidationError.Create(
                "pipeline.dmm.model.missing",
                "Model path must be provided for DMM comparison.");
        }

        if (string.IsNullOrWhiteSpace(request.Scope.ProfilePath))
        {
            return ValidationError.Create(
                "pipeline.dmm.profile.missing",
                "Profile path must be provided for DMM comparison.");
        }

        if (string.IsNullOrWhiteSpace(request.DmmPath))
        {
            return ValidationError.Create(
                "pipeline.dmm.script.missing",
                "DMM script path must be provided for comparison.");
        }

        var isSsdtProject = Directory.Exists(request.DmmPath);
        var isDmmScript = File.Exists(request.DmmPath);

        if (!isSsdtProject && !isDmmScript)
        {
            return ValidationError.Create(
                "pipeline.dmm.script.notFound",
                $"DMM script or SSDT directory '{request.DmmPath}' was not found.");
        }

        var log = new PipelineExecutionLogBuilder(_timeProvider);
        var telemetry = new PipelineBootstrapTelemetry(
            "Received dmm-compare pipeline request.",
            new PipelineLogMetadataBuilder()
                .WithPath("model", request.Scope.ModelPath)
                .WithPath("profile", request.Scope.ProfilePath)
                .WithPath("baseline", request.DmmPath)
                .WithValue("baseline.type", isSsdtProject ? "ssdt" : "dmm")
                .WithFlag("moduleFilter.hasFilter", request.Scope.ModuleFilter.HasFilter)
                .WithCount("moduleFilter.modules", request.Scope.ModuleFilter.Modules.Length)
                .WithValue("tightening.mode", request.Scope.TighteningOptions.Policy.Mode.ToString())
                .WithMetric("tightening.nullBudget", request.Scope.TighteningOptions.Policy.NullBudget)
                .WithFlag("emission.includePlatformAutoIndexes", request.Scope.SmoOptions.IncludePlatformAutoIndexes)
                .WithFlag("emission.emitBareTableOnly", request.Scope.SmoOptions.EmitBareTableOnly)
                .WithFlag("emission.sanitizeModuleNames", request.Scope.SmoOptions.SanitizeModuleNames)
                .Build(),
            "Loading profiling snapshot from fixtures.",
            new PipelineLogMetadataBuilder()
                .WithPath("profile", request.Scope.ProfilePath)
                .Build(),
            "Loaded profiling snapshot.");

        var bootstrapRequest = new PipelineBootstrapRequest(
            request.Scope.ModelPath,
            request.Scope.ModuleFilter,
            request.Scope.SupplementalModels,
            telemetry,
            (_, token) => new FixtureDataProfiler(request.Scope.ProfilePath, _profileSnapshotDeserializer)
                .CaptureAsync(token));

        var bootstrapAndCacheResult = await _bootstrapper
            .BootstrapAsync(log, bootstrapRequest, cancellationToken)
            .BindAsync(
                (bootstrapContext, token) => _evidenceCacheCoordinator
                    .CacheAsync(request.EvidenceCache, log, token)
                    .MapAsync(cache => (bootstrapContext, cache)),
                cancellationToken)
            .ConfigureAwait(false);

        if (bootstrapAndCacheResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(bootstrapAndCacheResult.Errors);
        }

        var (bootstrapContext, evidenceCache) = bootstrapAndCacheResult.Value;
        var filteredModel = bootstrapContext.FilteredModel;
        var supplementalEntities = bootstrapContext.SupplementalEntities;
        var profile = bootstrapContext.Profile;
        var pipelineWarnings = bootstrapContext.Warnings;

        var decisions = _tighteningPolicy.Decide(filteredModel, profile, request.Scope.TighteningOptions);
        var smoModel = _smoModelFactory.Create(
            filteredModel,
            decisions,
            profile,
            request.Scope.SmoOptions,
            supplementalEntities,
            request.Scope.TypeMappingPolicy);
        var smoTableCount = smoModel.Tables.Length;
        var smoColumnCount = smoModel.Tables.Sum(static table => table.Columns.Length);
        var smoIndexCount = smoModel.Tables.Sum(static table => table.Indexes.Length);
        var smoForeignKeyCount = smoModel.Tables.Sum(static table => table.ForeignKeys.Length);
        log.Record(
            "smo.model.created",
            "Materialized SMO table graph for comparison.",
            new PipelineLogMetadataBuilder()
                .WithCount("tables", smoTableCount)
                .WithCount("columns", smoColumnCount)
                .WithCount("indexes", smoIndexCount)
                .WithCount("foreignKeys", smoForeignKeyCount)
                .Build());

        var projectedResult = _smoLens.Project(new SmoDmmLensRequest(smoModel, request.Scope.SmoOptions));
        if (projectedResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(projectedResult.Errors);
        }

        var modelTables = projectedResult.Value;
        log.Record(
            "smo.model.projected",
            "Projected SMO model into DMM comparison shape.",
            new PipelineLogMetadataBuilder()
                .WithCount("tables", modelTables.Count)
                .Build());

        DmmComparisonFeatures comparisonFeatures;
        IReadOnlyList<DmmTable> dmmTables;
        SsdtTableLayoutComparisonResult? layoutComparison = null;

        if (isSsdtProject)
        {
            var parseResult = _ssdtLens.Project(request.DmmPath);
            if (parseResult.IsFailure)
            {
                return Result<DmmComparePipelineResult>.Failure(parseResult.Errors);
            }

            dmmTables = parseResult.Value;
            comparisonFeatures = DmmComparisonFeatures.Columns | DmmComparisonFeatures.PrimaryKeys | DmmComparisonFeatures.Indexes | DmmComparisonFeatures.ForeignKeys | DmmComparisonFeatures.ExtendedProperties;
            log.Record(
                "dmm.ssdt.parsed",
                "Parsed SSDT project baseline.",
                new PipelineLogMetadataBuilder()
                    .WithCount("tables", dmmTables.Count)
                    .Build());

            layoutComparison = _ssdtLayoutComparator.Compare(smoModel, request.Scope.SmoOptions, request.DmmPath);
            log.Record(
                "dmm.ssdt.layout",
                "Validated SSDT project table layout.",
                new PipelineLogMetadataBuilder()
                    .WithFlag("layout.match", layoutComparison.IsMatch)
                    .WithCount("layout.modelDifferences", layoutComparison.ModelDifferences.Count)
                    .WithCount("layout.ssdtDifferences", layoutComparison.SsdtDifferences.Count)
                    .Build());
        }
        else
        {
            using var reader = File.OpenText(request.DmmPath);
            var parseResult = _dmmScriptLens.Project(reader);
            if (parseResult.IsFailure)
            {
                return Result<DmmComparePipelineResult>.Failure(parseResult.Errors);
            }

            dmmTables = parseResult.Value;
            comparisonFeatures = DmmComparisonFeatures.Columns | DmmComparisonFeatures.PrimaryKeys;
            log.Record(
                "dmm.script.parsed",
                "Parsed DMM baseline script.",
                new PipelineLogMetadataBuilder()
                    .WithCount("tables", dmmTables.Count)
                    .Build());
        }

        var comparison = _dmmComparator.Compare(modelTables, dmmTables, comparisonFeatures);

        if (layoutComparison is { } layout)
        {
            if (layout.ModelDifferences.Count > 0 || layout.SsdtDifferences.Count > 0)
            {
                var mergedModelDifferences = comparison.ModelDifferences
                    .Concat(layout.ModelDifferences)
                    .ToArray();
                var mergedSsdtDifferences = comparison.SsdtDifferences
                    .Concat(layout.SsdtDifferences)
                    .ToArray();

                comparison = new DmmComparisonResult(
                    comparison.IsMatch && layout.IsMatch,
                    mergedModelDifferences,
                    mergedSsdtDifferences);
            }
            else if (!layout.IsMatch)
            {
                comparison = new DmmComparisonResult(
                    comparison.IsMatch && layout.IsMatch,
                    comparison.ModelDifferences,
                    comparison.SsdtDifferences);
            }
        }
        log.Record(
            "dmm.comparison.completed",
            "Compared SMO output against DMM baseline.",
            new PipelineLogMetadataBuilder()
                .WithFlag("comparison.match", comparison.IsMatch)
                .WithCount("comparison.modelDifferences", comparison.ModelDifferences.Count)
                .WithCount("comparison.ssdtDifferences", comparison.SsdtDifferences.Count)
                .Build());

        var diffPath = await _diffLogWriter.WriteAsync(
            request.DiffOutputPath,
            request.Scope.ModelPath,
            request.Scope.ProfilePath,
            request.DmmPath,
            comparison,
            cancellationToken).ConfigureAwait(false);
        log.Record(
            "dmm.diff.persisted",
            "Persisted DMM comparison diff artifact.",
            new PipelineLogMetadataBuilder()
                .WithPath("diff", diffPath)
                .Build());

        log.Record(
            "pipeline.completed",
            "DMM comparison pipeline completed successfully.",
            new PipelineLogMetadataBuilder()
                .WithPath("diff", diffPath)
                .WithPath("cache.directory", evidenceCache?.CacheDirectory)
                .Build());

        return new DmmComparePipelineResult(
            profile,
            comparison,
            diffPath,
            evidenceCache,
            log.Build(),
            pipelineWarnings);
    }
}
