using System;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Hosting;

/// <summary>
/// Represents a pipeline verb that can be executed with an arbitrary options payload.
/// </summary>
public interface IPipelineVerb
{
    string Name { get; }

    Type OptionsType { get; }

    Task<PipelineVerbResult> RunAsync(object options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Strongly typed contract for executing a pipeline verb.
/// </summary>
/// <typeparam name="TOptions">The options type consumed by the verb.</typeparam>
public interface IPipelineVerb<in TOptions> : IPipelineVerb
{
    Task<PipelineVerbResult> RunAsync(TOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class that wires the non-generic and generic verb contracts together.
/// </summary>
/// <typeparam name="TOptions">The options type consumed by the verb.</typeparam>
public abstract class PipelineVerb<TOptions> : IPipelineVerb<TOptions>
{
    public abstract string Name { get; }

    public Type OptionsType => typeof(TOptions);

    public Task<PipelineVerbResult> RunAsync(object options, CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options is not TOptions typedOptions)
        {
            throw new ArgumentException($"Options must be of type {typeof(TOptions)}.", nameof(options));
        }

        return RunAsync(typedOptions, cancellationToken);
    }

    public Task<PipelineVerbResult> RunAsync(TOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return RunInternalAsync(options, cancellationToken);
    }

    protected abstract Task<PipelineVerbResult> RunInternalAsync(TOptions options, CancellationToken cancellationToken);
}
