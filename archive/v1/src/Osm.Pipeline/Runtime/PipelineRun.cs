using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Runtime;

/// <summary>
/// Strongly typed representation of a pipeline verb execution.
/// </summary>
/// <typeparam name="TResult">Result payload type produced by the verb.</typeparam>
public sealed record PipelineRun<TResult>(
    string Verb,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    Result<TResult> Outcome,
    IReadOnlyList<PipelineArtifact> Artifacts,
    IReadOnlyDictionary<string, string?> Metadata) : IPipelineRun
{
    public bool IsSuccess => Outcome.IsSuccess;

    public ImmutableArray<ValidationError> Errors => Outcome.Errors;

    public TResult Value => Outcome.Value;

    object? IPipelineRun.Payload => Outcome.IsSuccess ? Outcome.Value : null;
}
