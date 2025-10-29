using System;
using System.Collections.Generic;
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

        if (string.IsNullOrWhiteSpace(request.ModelPath))
        {
            return ValidationError.Create(
                "pipeline.dmm.model.missing",
                "Model path must be provided for DMM comparison.");
        }

        if (string.IsNullOrWhiteSpace(request.ProfilePath))
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
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["modelPath"] = request.ModelPath,
                ["profilePath"] = request.ProfilePath,
                ["dmmPath"] = request.DmmPath,
                ["baseline.type"] = isSsdtProject ? "ssdt" : "dmm",
                ["moduleFilter.hasFilter"] = request.ModuleFilter.HasFilter ? "true" : "false",
                ["moduleFilter.moduleCount"] = request.ModuleFilter.Modules.Length.ToString(CultureInfo.InvariantCulture),
                ["tightening.mode"] = request.TighteningOptions.Policy.Mode.ToString(),
                ["tightening.nullBudget"] = request.TighteningOptions.Policy.NullBudget.ToString(CultureInfo.InvariantCulture),
                ["emission.includePlatformAutoIndexes"] = request.SmoOptions.IncludePlatformAutoIndexes ? "true" : "false",
                ["emission.emitBareTableOnly"] = request.SmoOptions.EmitBareTableOnly ? "true" : "false",
                ["emission.sanitizeModuleNames"] = request.SmoOptions.SanitizeModuleNames ? "true" : "false"
            },
            "Loading profiling snapshot from fixtures.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["profilePath"] = request.ProfilePath
            },
            "Loaded profiling snapshot.");

        var bootstrapRequest = new PipelineBootstrapRequest(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            telemetry,
            (_, token) => new FixtureDataProfiler(request.ProfilePath, _profileSnapshotDeserializer)
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

        var decisions = _tighteningPolicy.Decide(filteredModel, profile, request.TighteningOptions);
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
            "Materialized SMO table graph for comparison.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tables"] = smoTableCount.ToString(CultureInfo.InvariantCulture),
                ["columns"] = smoColumnCount.ToString(CultureInfo.InvariantCulture),
                ["indexes"] = smoIndexCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = smoForeignKeyCount.ToString(CultureInfo.InvariantCulture)
            });

        var projectedResult = _smoLens.Project(new SmoDmmLensRequest(smoModel, request.SmoOptions));
        if (projectedResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(projectedResult.Errors);
        }

        var modelTables = projectedResult.Value;
        log.Record(
            "smo.model.projected",
            "Projected SMO model into DMM comparison shape.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tableCount"] = modelTables.Count.ToString(CultureInfo.InvariantCulture)
            });

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
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["tableCount"] = dmmTables.Count.ToString(CultureInfo.InvariantCulture)
                });

            layoutComparison = _ssdtLayoutComparator.Compare(smoModel, request.SmoOptions, request.DmmPath);
            log.Record(
                "dmm.ssdt.layout",
                "Validated SSDT project table layout.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["isMatch"] = layoutComparison.IsMatch ? "true" : "false",
                    ["modelDifferences"] = layoutComparison.ModelDifferences.Count.ToString(CultureInfo.InvariantCulture),
                    ["ssdtDifferences"] = layoutComparison.SsdtDifferences.Count.ToString(CultureInfo.InvariantCulture)
                });
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
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["tableCount"] = dmmTables.Count.ToString(CultureInfo.InvariantCulture)
                });
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
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["isMatch"] = comparison.IsMatch ? "true" : "false",
                ["modelDifferences"] = comparison.ModelDifferences.Count.ToString(CultureInfo.InvariantCulture),
                ["ssdtDifferences"] = comparison.SsdtDifferences.Count.ToString(CultureInfo.InvariantCulture)
            });

        var diffPath = await _diffLogWriter.WriteAsync(
            request.DiffOutputPath,
            request.ModelPath,
            request.ProfilePath,
            request.DmmPath,
            comparison,
            cancellationToken).ConfigureAwait(false);
        log.Record(
            "dmm.diff.persisted",
            "Persisted DMM comparison diff artifact.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["path"] = diffPath
            });

        log.Record(
            "pipeline.completed",
            "DMM comparison pipeline completed successfully.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["diffPath"] = diffPath,
                ["cacheDirectory"] = evidenceCache?.CacheDirectory
            });

        return new DmmComparePipelineResult(
            profile,
            comparison,
            diffPath,
            evidenceCache,
            log.Build(),
            pipelineWarnings);
    }
}
