using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Profiling;
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
    Func<OsmModel, CancellationToken, Task<Result<ProfileCaptureResult>>> ProfileCaptureAsync,
    OsmModel? InlineModel = null,
    ImmutableArray<string> ModelWarnings = default);

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
    ImmutableArray<string> Warnings,
    MultiEnvironmentProfileReport? MultiEnvironmentReport);

public sealed class PipelineBootstrapper : IPipelineBootstrapper
{
    private readonly ModelLoader _modelLoader;
    private readonly ModuleFilterRunner _moduleFilterRunner;
    private readonly SupplementalLoader _supplementalLoader;
    private readonly ProfilerRunner _profilerRunner;

    public PipelineBootstrapper(
        IModelIngestionService modelIngestionService,
        ModuleFilter moduleFilter,
        SupplementalEntityLoader supplementalLoader,
        IProfilingInsightGenerator insightGenerator)
    {
        if (modelIngestionService is null)
        {
            throw new ArgumentNullException(nameof(modelIngestionService));
        }

        if (moduleFilter is null)
        {
            throw new ArgumentNullException(nameof(moduleFilter));
        }

        if (supplementalLoader is null)
        {
            throw new ArgumentNullException(nameof(supplementalLoader));
        }

        if (insightGenerator is null)
        {
            throw new ArgumentNullException(nameof(insightGenerator));
        }

        _modelLoader = new ModelLoader(modelIngestionService);
        _moduleFilterRunner = new ModuleFilterRunner(moduleFilter);
        _supplementalLoader = new SupplementalLoader(supplementalLoader);
        _profilerRunner = new ProfilerRunner(insightGenerator);
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

        var context = new BootstrapPipelineContext(log, request);
        context.RecordRequestTelemetry();

        var modelResult = await _modelLoader
            .LoadAsync(context, cancellationToken)
            .ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(modelResult.Errors);
        }

        var filterResult = _moduleFilterRunner.Run(modelResult.Value);
        if (filterResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(filterResult.Errors);
        }

        var supplementalResult = await _supplementalLoader
            .LoadAsync(filterResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (supplementalResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(supplementalResult.Errors);
        }

        var profilerResult = await _profilerRunner
            .RunAsync(supplementalResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (profilerResult.IsFailure)
        {
            return Result<PipelineBootstrapContext>.Failure(profilerResult.Errors);
        }

        return Result<PipelineBootstrapContext>.Success(profilerResult.Value.BuildResult());
    }
}
