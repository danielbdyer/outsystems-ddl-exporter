using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Runtime;

/// <summary>
/// Base helper for pipeline verbs that captures execution timing and artifact projection.
/// </summary>
/// <typeparam name="TOptions">Options record type used to configure the verb.</typeparam>
/// <typeparam name="TResult">Result payload produced by the verb.</typeparam>
public abstract class PipelineVerb<TOptions, TResult> : IPipelineVerb
    where TOptions : class
{
    private readonly TimeProvider _timeProvider;

    protected PipelineVerb(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public abstract string Name { get; }

    public Type OptionsType => typeof(TOptions);

    async Task<IPipelineRun> IPipelineVerb.RunAsync(object options, CancellationToken cancellationToken)
    {
        if (options is not TOptions typedOptions)
        {
            throw new ArgumentException($"Expected options of type {typeof(TOptions).FullName}.", nameof(options));
        }

        return await RunInternalAsync(typedOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IPipelineRun> RunInternalAsync(TOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = _timeProvider.GetUtcNow();
        var outcome = await ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        var completedAt = _timeProvider.GetUtcNow();

        IReadOnlyList<PipelineArtifact> artifacts = Array.Empty<PipelineArtifact>();
        if (outcome.IsSuccess)
        {
            artifacts = DescribeArtifacts(outcome.Value);
        }

        var metadata = BuildMetadata(options, outcome);

        return new PipelineRun<TResult>(
            Name,
            startedAt,
            completedAt,
            outcome,
            artifacts,
            metadata);
    }

    protected abstract Task<Result<TResult>> ExecuteAsync(TOptions options, CancellationToken cancellationToken);

    protected virtual IReadOnlyList<PipelineArtifact> DescribeArtifacts(TResult result)
        => Array.Empty<PipelineArtifact>();

    protected virtual IReadOnlyDictionary<string, string?> BuildMetadata(TOptions options, Result<TResult> outcome)
        => ImmutableDictionary<string, string?>.Empty;
}
