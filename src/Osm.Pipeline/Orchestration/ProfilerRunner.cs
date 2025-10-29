using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Validation.Profiling;

namespace Osm.Pipeline.Orchestration;

internal sealed class ProfilerRunner
{
    private readonly IProfilingInsightGenerator _insightGenerator;

    public ProfilerRunner(IProfilingInsightGenerator insightGenerator)
    {
        _insightGenerator = insightGenerator ?? throw new ArgumentNullException(nameof(insightGenerator));
    }

    public async Task<Result<BootstrapPipelineContext>> RunAsync(
        BootstrapPipelineContext context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.FilteredModel is null)
        {
            throw new InvalidOperationException("Filtered model must be available before profiling.");
        }

        if (context.Request.ProfileCaptureAsync is null)
        {
            throw new InvalidOperationException("Profile capture strategy must be provided.");
        }

        context.LogProfilingStarted();

        var profileResult = await context.Request.ProfileCaptureAsync(context.FilteredModel, cancellationToken)
            .ConfigureAwait(false);

        if (profileResult.IsFailure)
        {
            return Result<BootstrapPipelineContext>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;
        context.SetProfile(profile);
        context.SetInsights(_insightGenerator.Generate(profile));

        return Result<BootstrapPipelineContext>.Success(context);
    }
}
