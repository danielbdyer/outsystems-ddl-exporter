using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.ModelIngestion;
using Osm.Json;
using Osm.Validation.Profiling;

namespace Osm.Pipeline.Orchestration;

public interface IPipelineBootstrapper
{
    Task<Result<PipelineBootstrapContext>> BootstrapAsync(
        PipelineExecutionLogBuilder log,
        PipelineBootstrapRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PipelineBootstrapRequest(
    string ModelPath,
    ModuleFilterOptions ModuleFilter,
    SupplementalModelOptions SupplementalModels,
    PipelineBootstrapTelemetry Telemetry,
    Func<OsmModel, CancellationToken, Task<Result<ProfileSnapshot>>> ProfileCaptureAsync);

public sealed record PipelineBootstrapTelemetry(
    string RequestMessage,
    IReadOnlyDictionary<string, string?> RequestMetadata,
    string ProfilingStartMessage,
    IReadOnlyDictionary<string, string?> ProfilingStartMetadata,
    string ProfilingCompletedMessage);

public sealed record PipelineBootstrapContext(
    OsmModel FilteredModel,
    ImmutableArray<EntityModel> SupplementalEntities,
    ProfileSnapshot Profile,
    ImmutableArray<ProfilingInsight> Insights,
    ImmutableArray<string> Warnings);

public sealed class PipelineBootstrapper : IPipelineBootstrapper
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ModuleFilter _moduleFilter;
    private readonly SupplementalEntityLoader _supplementalLoader;
    private readonly IProfilingInsightGenerator _insightGenerator;

    public PipelineBootstrapper(
        IModelIngestionService modelIngestionService,
        ModuleFilter moduleFilter,
        SupplementalEntityLoader supplementalLoader,
        IProfilingInsightGenerator insightGenerator)
    {
        _modelIngestionService = modelIngestionService ?? throw new ArgumentNullException(nameof(modelIngestionService));
        _moduleFilter = moduleFilter ?? throw new ArgumentNullException(nameof(moduleFilter));
        _supplementalLoader = supplementalLoader ?? throw new ArgumentNullException(nameof(supplementalLoader));
        _insightGenerator = insightGenerator ?? throw new ArgumentNullException(nameof(insightGenerator));
    }

    public async Task<Result<PipelineBootstrapContext>> BootstrapAsync(
        PipelineExecutionLogBuilder log,
        PipelineBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.ProfileCaptureAsync is null)
        {
            throw new ArgumentException("Profile capture strategy must be provided.", nameof(request));
        }

        log.Record("request.received", request.Telemetry.RequestMessage, request.Telemetry.RequestMetadata);

        var pipelineWarnings = ImmutableArray.CreateBuilder<string>();
        var ingestionWarnings = new List<string>();
        var ingestionOptions = new ModelIngestionOptions(request.ModuleFilter.ValidationOverrides, null);

        var modelResult = await _modelIngestionService
            .LoadFromFileAsync(request.ModelPath, ingestionWarnings, cancellationToken, ingestionOptions)
            .ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(modelResult.Errors);
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
            return Result<PipelineBootstrapContext>.Failure(filteredResult.Errors);
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

        var supplementalResult = await _supplementalLoader
            .LoadAsync(request.SupplementalModels, cancellationToken)
            .ConfigureAwait(false);
        if (supplementalResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(supplementalResult.Errors);
        }

        var supplementalEntities = supplementalResult.Value;
        log.Record(
            "supplemental.loaded",
            "Loaded supplemental entity definitions.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["supplementalEntityCount"] = supplementalEntities.Length.ToString(CultureInfo.InvariantCulture),
                ["requestedPaths"] = request.SupplementalModels.Paths.Count.ToString(CultureInfo.InvariantCulture)
            });

        log.Record(
            "profiling.capture.start",
            request.Telemetry.ProfilingStartMessage,
            request.Telemetry.ProfilingStartMetadata);

        var profileResult = await request.ProfileCaptureAsync(filteredModel, cancellationToken).ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;
        log.Record(
            "profiling.capture.completed",
            request.Telemetry.ProfilingCompletedMessage,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["columnProfiles"] = profile.Columns.Length.ToString(CultureInfo.InvariantCulture),
                ["uniqueCandidates"] = profile.UniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
                ["compositeUniqueCandidates"] = profile.CompositeUniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = profile.ForeignKeys.Length.ToString(CultureInfo.InvariantCulture)
            });

        var insights = _insightGenerator.Generate(profile);

        return new PipelineBootstrapContext(
            filteredModel,
            supplementalEntities,
            profile,
            insights,
            pipelineWarnings.ToImmutable());
    }
}
