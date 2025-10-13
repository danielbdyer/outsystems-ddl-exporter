using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers;

public interface IPipelineStep<in TContext>
{
    string Name { get; }
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken);
}

public interface IPipeline<in TContext>
{
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken);
}

internal sealed class PipelineBuilder<TContext>
{
    private readonly List<PipelineStepRegistration<TContext>> _steps = new();

    public PipelineBuilder<TContext> Then(IPipelineStep<TContext> step, Func<TContext, bool>? predicate = null)
    {
        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        _steps.Add(new PipelineStepRegistration<TContext>(step, predicate));
        return this;
    }

    public IPipeline<TContext> Build()
    {
        return new Pipeline<TContext>(_steps.ToArray());
    }
}

internal sealed record PipelineStepRegistration<TContext>(
    IPipelineStep<TContext> Step,
    Func<TContext, bool>? Predicate);

internal sealed class Pipeline<TContext> : IPipeline<TContext>
{
    private readonly IReadOnlyList<PipelineStepRegistration<TContext>> _steps;

    public Pipeline(IReadOnlyList<PipelineStepRegistration<TContext>> steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        foreach (var registration in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (registration.Predicate is not null && !registration.Predicate(context))
            {
                continue;
            }

            await registration.Step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
