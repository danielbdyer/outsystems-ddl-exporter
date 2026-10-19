using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILoggerFactory _loggerFactory;

    public PipelineBuilder(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

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
        return new Pipeline<TContext>(_steps.ToArray(), _loggerFactory);
    }
}

internal sealed record PipelineStepRegistration<TContext>(
    IPipelineStep<TContext> Step,
    Func<TContext, bool>? Predicate);

internal sealed class Pipeline<TContext> : IPipeline<TContext>
{
    private readonly IReadOnlyList<PipelineStepRegistration<TContext>> _steps;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public Pipeline(IReadOnlyList<PipelineStepRegistration<TContext>> steps, ILoggerFactory loggerFactory)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger($"Pipeline<{typeof(TContext).Name}>");
    }

    public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation("Executing pipeline with {StepCount} steps.", _steps.Count);

        foreach (var registration in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (registration.Predicate is not null && !registration.Predicate(context))
            {
                _logger.LogInformation("Skipping step {StepName} because predicate returned false.", registration.Step.Name);
                continue;
            }

            _logger.LogInformation("Starting step {StepName}.", registration.Step.Name);

            try
            {
                await registration.Step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Completed step {StepName}.", registration.Step.Name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Pipeline cancelled while executing {StepName}.", registration.Step.Name);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step {StepName} failed.", registration.Step.Name);
                throw;
            }
        }

        _logger.LogInformation("Pipeline execution finished.");
    }
}
