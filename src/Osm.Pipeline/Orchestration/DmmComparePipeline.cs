using System;
using System.Collections.Generic;
using System.IO;
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

namespace Osm.Pipeline.Orchestration;

public sealed class DmmComparePipeline
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ModuleFilter _moduleFilter;
    private readonly SupplementalEntityLoader _supplementalLoader;
    private readonly TighteningPolicy _tighteningPolicy;
    private readonly SmoModelFactory _smoModelFactory;
    private readonly DmmParser _dmmParser;
    private readonly DmmComparator _dmmComparator;
    private readonly DmmDiffLogWriter _diffLogWriter;
    private readonly IEvidenceCacheService _evidenceCacheService;
    private readonly ProfileSnapshotDeserializer _profileSnapshotDeserializer;

    public DmmComparePipeline(
        IModelIngestionService? modelIngestionService = null,
        ModuleFilter? moduleFilter = null,
        SupplementalEntityLoader? supplementalLoader = null,
        TighteningPolicy? tighteningPolicy = null,
        SmoModelFactory? smoModelFactory = null,
        DmmParser? dmmParser = null,
        DmmComparator? dmmComparator = null,
        DmmDiffLogWriter? diffLogWriter = null,
        IEvidenceCacheService? evidenceCacheService = null,
        ProfileSnapshotDeserializer? profileSnapshotDeserializer = null)
    {
        _modelIngestionService = modelIngestionService ?? new ModelIngestionService(new ModelJsonDeserializer());
        _moduleFilter = moduleFilter ?? new ModuleFilter();
        _supplementalLoader = supplementalLoader ?? new SupplementalEntityLoader();
        _tighteningPolicy = tighteningPolicy ?? new TighteningPolicy();
        _smoModelFactory = smoModelFactory ?? new SmoModelFactory();
        _dmmParser = dmmParser ?? new DmmParser();
        _dmmComparator = dmmComparator ?? new DmmComparator();
        _diffLogWriter = diffLogWriter ?? new DmmDiffLogWriter();
        _evidenceCacheService = evidenceCacheService ?? new EvidenceCacheService();
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? new ProfileSnapshotDeserializer();
    }

    public async Task<Result<DmmComparePipelineResult>> ExecuteAsync(
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

        if (!File.Exists(request.DmmPath))
        {
            return ValidationError.Create(
                "pipeline.dmm.script.notFound",
                $"DMM script '{request.DmmPath}' was not found.");
        }

        var modelResult = await _modelIngestionService.LoadFromFileAsync(request.ModelPath, cancellationToken).ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(modelResult.Errors);
        }

        var filteredResult = _moduleFilter.Apply(modelResult.Value, request.ModuleFilter);
        if (filteredResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(filteredResult.Errors);
        }

        var supplementalResult = await _supplementalLoader.LoadAsync(request.SupplementalModels, cancellationToken).ConfigureAwait(false);
        if (supplementalResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(supplementalResult.Errors);
        }

        var profileResult = await new FixtureDataProfiler(request.ProfilePath, _profileSnapshotDeserializer)
            .CaptureAsync(cancellationToken)
            .ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(profileResult.Errors);
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
                return Result<DmmComparePipelineResult>.Failure(cacheExecution.Errors);
            }

            cacheResult = cacheExecution.Value;
        }

        var decisions = _tighteningPolicy.Decide(filteredResult.Value, profile, request.TighteningOptions);
        var smoModel = _smoModelFactory.Create(
            filteredResult.Value,
            decisions,
            profile,
            request.SmoOptions,
            supplementalResult.Value);

        using var reader = File.OpenText(request.DmmPath);
        var parseResult = _dmmParser.Parse(reader);
        if (parseResult.IsFailure)
        {
            return Result<DmmComparePipelineResult>.Failure(parseResult.Errors);
        }

        var comparison = _dmmComparator.Compare(smoModel, parseResult.Value, request.SmoOptions.NamingOverrides);

        var diffPath = await _diffLogWriter.WriteAsync(
            request.DiffOutputPath,
            request.ModelPath,
            request.ProfilePath,
            request.DmmPath,
            comparison,
            cancellationToken).ConfigureAwait(false);

        return new DmmComparePipelineResult(profile, comparison, diffPath, cacheResult);
    }
}
