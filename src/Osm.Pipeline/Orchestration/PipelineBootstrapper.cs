using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.ModelIngestion;
using Osm.Json;

namespace Osm.Pipeline.Orchestration;

public interface IPipelineBootstrapper
{
    Task<Result<PipelineBootstrapContext>> BootstrapAsync(
        PipelineBootstrapRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PipelineBootstrapRequest(
    string ModelPath,
    ModuleFilterOptions ModuleFilter,
    SupplementalModelOptions SupplementalModels,
    PipelineExecutionLogBuilder Log,
    ImmutableArray<string>.Builder Warnings,
    PipelineBootstrapProfilingStrategy ProfilingStrategy,
    ModelIngestionOptions? CustomIngestionOptions = null)
{
    public ModelIngestionOptions IngestionOptions
        => CustomIngestionOptions ?? new ModelIngestionOptions(ModuleFilter.ValidationOverrides, null);
}

public sealed record PipelineBootstrapContext(
    OsmModel FilteredModel,
    ProfileSnapshot Profile,
    ImmutableArray<EntityModel> SupplementalEntities,
    PipelineExecutionLogBuilder Log,
    ImmutableArray<string>.Builder Warnings);

public sealed record PipelineBootstrapProfilingStrategy(
    string StartMessage,
    Func<Dictionary<string, string?>>? StartMetadataFactory,
    string CompletedMessage,
    Action<ProfileSnapshot, Dictionary<string, string?>>? CompletedMetadataAugmentor,
    Func<OsmModel, CancellationToken, Task<Result<ProfileSnapshot>>> CaptureAsync);

public sealed class PipelineBootstrapper : IPipelineBootstrapper
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ModuleFilter _moduleFilter;
    private readonly SupplementalEntityLoader _supplementalLoader;

    public PipelineBootstrapper(
        IModelIngestionService? modelIngestionService = null,
        ModuleFilter? moduleFilter = null,
        SupplementalEntityLoader? supplementalLoader = null)
    {
        _modelIngestionService = modelIngestionService ?? new ModelIngestionService(new ModelJsonDeserializer());
        _moduleFilter = moduleFilter ?? new ModuleFilter();
        _supplementalLoader = supplementalLoader ?? new SupplementalEntityLoader();
    }

    public async Task<Result<PipelineBootstrapContext>> BootstrapAsync(
        PipelineBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.ProfilingStrategy is null)
        {
            throw new ArgumentException("Profiling strategy must be provided.", nameof(request));
        }

        var ingestionWarnings = new List<string>();
        var modelResult = await _modelIngestionService
            .LoadFromFileAsync(request.ModelPath, ingestionWarnings, cancellationToken, request.IngestionOptions)
            .ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(modelResult.Errors);
        }

        if (ingestionWarnings.Count > 0)
        {
            request.Warnings.AddRange(ingestionWarnings);
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

            request.Log.Record(
                "model.schema.warnings",
                "Model JSON schema validation produced warnings.",
                metadata);
        }

        var model = modelResult.Value;
        var moduleCount = model.Modules.Length;
        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(entity => entity.Attributes.Length));
        request.Log.Record(
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
            return Result<PipelineBootstrapContext>.Failure(filteredResult.Errors);
        }

        var filteredModel = filteredResult.Value;
        request.Log.Record(
            "model.filtered",
            "Applied module filter options.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["originalModules"] = moduleCount.ToString(CultureInfo.InvariantCulture),
                ["filteredModules"] = filteredModel.Modules.Length.ToString(CultureInfo.InvariantCulture),
                ["filter.includeSystemModules"] = request.ModuleFilter.IncludeSystemModules ? "true" : "false",
                ["filter.includeInactiveModules"] = request.ModuleFilter.IncludeInactiveModules ? "true" : "false"
            });

        var supplementalResult = await _supplementalLoader
            .LoadAsync(request.SupplementalModels, cancellationToken)
            .ConfigureAwait(false);
        if (supplementalResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(supplementalResult.Errors);
        }

        request.Log.Record(
            "supplemental.loaded",
            "Loaded supplemental entity definitions.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["supplementalEntityCount"] = supplementalResult.Value.Length.ToString(CultureInfo.InvariantCulture),
                ["requestedPaths"] = request.SupplementalModels.Paths.Count.ToString(CultureInfo.InvariantCulture)
            });

        var startMetadata = request.ProfilingStrategy.StartMetadataFactory?.Invoke()
            ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        request.Log.Record(
            "profiling.capture.start",
            request.ProfilingStrategy.StartMessage,
            startMetadata);

        var profileResult = await request.ProfilingStrategy
            .CaptureAsync(filteredModel, cancellationToken)
            .ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;
        var completedMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["columnProfiles"] = profile.Columns.Length.ToString(CultureInfo.InvariantCulture),
            ["uniqueCandidates"] = profile.UniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
            ["compositeUniqueCandidates"] = profile.CompositeUniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
            ["foreignKeys"] = profile.ForeignKeys.Length.ToString(CultureInfo.InvariantCulture)
        };
        request.ProfilingStrategy.CompletedMetadataAugmentor?.Invoke(profile, completedMetadata);
        request.Log.Record(
            "profiling.capture.completed",
            request.ProfilingStrategy.CompletedMessage,
            completedMetadata);

        return new PipelineBootstrapContext(
            filteredModel,
            profile,
            supplementalResult.Value,
            request.Log,
            request.Warnings);
    }
}
