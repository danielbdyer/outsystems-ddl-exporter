using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtBootstrapStep : IBuildSsdtStep<PipelineInitialized, BootstrapCompleted>
{
    private readonly IPipelineBootstrapper _bootstrapper;
    private readonly IDataProfilerFactory _profilerFactory;
    private readonly IPipelineBootstrapTelemetryFactory _telemetryFactory;

    public BuildSsdtBootstrapStep(
        IPipelineBootstrapper bootstrapper,
        IDataProfilerFactory profilerFactory,
        IPipelineBootstrapTelemetryFactory telemetryFactory)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _profilerFactory = profilerFactory ?? throw new ArgumentNullException(nameof(profilerFactory));
        _telemetryFactory = telemetryFactory ?? throw new ArgumentNullException(nameof(telemetryFactory));
    }

    public async Task<Result<BootstrapCompleted>> ExecuteAsync(
        PipelineInitialized state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var request = state.Request;
        var scope = ModelExecutionScope.Create(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            request.TighteningOptions,
            request.SmoOptions);
        var telemetry = _telemetryFactory.Create(
            scope,
            new PipelineCommandDescriptor(
                "Received build-ssdt pipeline request.",
                "Capturing profiling snapshot.",
                "Captured profiling snapshot.",
                IncludeSupplementalDetails: true,
                IncludeTighteningDetails: true,
                IncludeEmissionDetails: true),
            new PipelineBootstrapTelemetryExtras(
                ProfilerProvider: request.ProfilerProvider,
                ProfilePath: request.ProfilePath,
                OutputPath: request.OutputDirectory));
        var bootstrapRequest = new PipelineBootstrapRequest(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            telemetry,
            (model, token) => CaptureProfileAsync(request, model, token));

        var bootstrapResult = await _bootstrapper
            .BootstrapAsync(state.Log, bootstrapRequest, cancellationToken)
            .ConfigureAwait(false);
        if (bootstrapResult.IsFailure)
        {
            return Result<BootstrapCompleted>.Failure(bootstrapResult.Errors);
        }

        return Result<BootstrapCompleted>.Success(new BootstrapCompleted(
            state.Request,
            state.Log,
            bootstrapResult.Value));
    }

    private async Task<Result<ProfileSnapshot>> CaptureProfileAsync(
        BuildSsdtPipelineRequest request,
        OsmModel model,
        CancellationToken cancellationToken)
    {
        var profilerResult = _profilerFactory.Create(request, model);
        if (profilerResult.IsFailure)
        {
            return Result<ProfileSnapshot>.Failure(profilerResult.Errors);
        }

        return await profilerResult.Value.CaptureAsync(cancellationToken).ConfigureAwait(false);
    }
}
