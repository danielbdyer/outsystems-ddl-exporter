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

    public BuildSsdtBootstrapStep(
        IPipelineBootstrapper bootstrapper,
        IDataProfilerFactory profilerFactory)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _profilerFactory = profilerFactory ?? throw new ArgumentNullException(nameof(profilerFactory));
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
        var telemetry = CreateTelemetry(request);
        var bootstrapRequest = new PipelineBootstrapRequest(
            request.Scope.ModelPath,
            request.Scope.ModuleFilter,
            request.Scope.SupplementalModels,
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

    private static PipelineBootstrapTelemetry CreateTelemetry(BuildSsdtPipelineRequest request)
    {
        return new PipelineBootstrapTelemetry(
            "Received build-ssdt pipeline request.",
            new PipelineLogMetadataBuilder()
                .WithPath("model", request.Scope.ModelPath)
                .WithPath("output", request.OutputDirectory)
                .WithValue("profiling.provider", request.ProfilerProvider)
                .WithFlag("moduleFilter.hasFilter", request.Scope.ModuleFilter.HasFilter)
                .WithCount("moduleFilter.modules", request.Scope.ModuleFilter.Modules.Length)
                .WithFlag("supplemental.includeUsers", request.Scope.SupplementalModels.IncludeUsers)
                .WithCount("supplemental.paths", request.Scope.SupplementalModels.Paths.Count)
                .WithValue("tightening.mode", request.Scope.TighteningOptions.Policy.Mode.ToString())
                .WithMetric("tightening.nullBudget", request.Scope.TighteningOptions.Policy.NullBudget)
                .WithFlag("emission.includePlatformAutoIndexes", request.Scope.SmoOptions.IncludePlatformAutoIndexes)
                .WithFlag("emission.emitBareTableOnly", request.Scope.SmoOptions.EmitBareTableOnly)
                .WithFlag("emission.sanitizeModuleNames", request.Scope.SmoOptions.SanitizeModuleNames)
                .WithCount("emission.moduleParallelism", request.Scope.SmoOptions.ModuleParallelism)
                .Build(),
            "Capturing profiling snapshot.",
            new PipelineLogMetadataBuilder()
                .WithValue("profiling.provider", request.ProfilerProvider)
                .WithPath("profile", request.Scope.ProfilePath)
                .Build(),
            "Captured profiling snapshot.");
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
