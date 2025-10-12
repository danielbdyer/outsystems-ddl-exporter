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
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Profiling;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Pipeline.Mediation;

namespace Osm.Pipeline.Orchestration;

public sealed class DmmComparePipeline : ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ModuleFilter _moduleFilter;
    private readonly SupplementalEntityLoader _supplementalLoader;
    private readonly TighteningPolicy _tighteningPolicy;
    private readonly SmoModelFactory _smoModelFactory;
    private readonly IDmmLens<TextReader> _dmmScriptLens;
    private readonly IDmmLens<SmoDmmLensRequest> _smoLens;
    private readonly IDmmLens<string> _ssdtLens;
    private readonly DmmComparator _dmmComparator;
    private readonly SsdtTableLayoutComparator _ssdtLayoutComparator;
    private readonly DmmDiffLogWriter _diffLogWriter;
    private readonly IEvidenceCacheService _evidenceCacheService;
    private readonly ProfileSnapshotDeserializer _profileSnapshotDeserializer;

    public DmmComparePipeline(
        IModelIngestionService? modelIngestionService = null,
        ModuleFilter? moduleFilter = null,
        SupplementalEntityLoader? supplementalLoader = null,
        TighteningPolicy? tighteningPolicy = null,
        SmoModelFactory? smoModelFactory = null,
        IDmmLens<TextReader>? dmmScriptLens = null,
        IDmmLens<SmoDmmLensRequest>? smoLens = null,
        IDmmLens<string>? ssdtLens = null,
        DmmComparator? dmmComparator = null,
        SsdtTableLayoutComparator? ssdtLayoutComparator = null,
        DmmDiffLogWriter? diffLogWriter = null,
        IEvidenceCacheService? evidenceCacheService = null,
        ProfileSnapshotDeserializer? profileSnapshotDeserializer = null)
    {
        _modelIngestionService = modelIngestionService ?? new ModelIngestionService(new ModelJsonDeserializer());
        _moduleFilter = moduleFilter ?? new ModuleFilter();
        _supplementalLoader = supplementalLoader ?? new SupplementalEntityLoader();
        _tighteningPolicy = tighteningPolicy ?? new TighteningPolicy();
        _smoModelFactory = smoModelFactory ?? new SmoModelFactory();
        _dmmScriptLens = dmmScriptLens ?? new ScriptDomDmmLens();
        _smoLens = smoLens ?? new SmoDmmLens();
        _ssdtLens = ssdtLens ?? new SsdtProjectDmmLens();
        _dmmComparator = dmmComparator ?? new DmmComparator();
        _ssdtLayoutComparator = ssdtLayoutComparator ?? new SsdtTableLayoutComparator();
        _diffLogWriter = diffLogWriter ?? new DmmDiffLogWriter();
        _evidenceCacheService = evidenceCacheService ?? new EvidenceCacheService();
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? new ProfileSnapshotDeserializer();
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

        var log = new PipelineExecutionLogBuilder();
        var pipelineWarnings = ImmutableArray.CreateBuilder<string>();
        log.Record(
            "request.received",
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
            });

        var ingestionWarnings = new List<string>();
        var modelResult = await _modelIngestionService
            .LoadFromFileAsync(request.ModelPath, ingestionWarnings, cancellationToken)
            .ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(modelResult.Errors);
        }

        if (ingestionWarnings.Count > 0)
        {
            pipelineWarnings.AddRange(ingestionWarnings);
            var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["summary"] = ingestionWarnings[0],
                ["lineCount"] = ingestionWarnings.Count.ToString(CultureInfo.InvariantCulture)
            };

            if (ingestionWarnings.Count > 1)
            {
                metadata["example1"] = ingestionWarnings[1];
            }

            if (ingestionWarnings.Count > 2)
            {
                metadata["example2"] = ingestionWarnings[2];
            }

            if (ingestionWarnings.Count > 3)
            {
                metadata["example3"] = ingestionWarnings[3];
            }

            if (ingestionWarnings.Count > 4)
            {
                metadata["suppressed"] = ingestionWarnings[^1];
            }

            log.Record(
                "model.schema.warnings",
                "Model JSON schema validation produced warnings.",
                metadata);
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
            return Result<DmmComparePipelineResult>.Failure(filteredResult.Errors);
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
            return Result<DmmComparePipelineResult>.Failure(supplementalResult.Errors);
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
            "Loading profiling snapshot from fixtures.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["profilePath"] = request.ProfilePath
            });

        var profileResult = await new FixtureDataProfiler(request.ProfilePath, _profileSnapshotDeserializer)
            .CaptureAsync(cancellationToken)
            .ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;
        log.Record(
            "profiling.capture.completed",
            "Loaded profiling snapshot.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
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
                return Result<DmmComparePipelineResult>.Failure(cacheExecution.Errors);
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
        var smoModel = _smoModelFactory.Create(
            filteredModel,
            decisions,
            profile,
            request.SmoOptions,
            supplementalResult.Value,
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
                ["cacheDirectory"] = cacheResult?.CacheDirectory
            });

        return new DmmComparePipelineResult(
            profile,
            comparison,
            diffPath,
            cacheResult,
            log.Build(),
            pipelineWarnings.ToImmutable());
    }
}
