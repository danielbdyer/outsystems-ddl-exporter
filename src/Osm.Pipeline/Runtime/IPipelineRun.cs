using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Runtime;

/// <summary>
/// Represents the outcome of a pipeline verb invocation.
/// </summary>
public interface IPipelineRun
{
    /// <summary>
    /// Gets the canonical verb name.
    /// </summary>
    string Verb { get; }

    /// <summary>
    /// Gets the UTC timestamp when execution started.
    /// </summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets the UTC timestamp when execution completed.
    /// </summary>
    DateTimeOffset CompletedAt { get; }

    /// <summary>
    /// Gets the artifacts that were produced during the run.
    /// </summary>
    IReadOnlyList<PipelineArtifact> Artifacts { get; }

    /// <summary>
    /// Gets metadata about the invocation (configuration paths, model sources, etc.).
    /// </summary>
    IReadOnlyDictionary<string, string?> Metadata { get; }

    /// <summary>
    /// Gets a value indicating whether the verb completed successfully.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// Gets the validation errors emitted by the verb.
    /// </summary>
    ImmutableArray<ValidationError> Errors { get; }

    /// <summary>
    /// Gets the payload returned by the verb when the run succeeded.
    /// </summary>
    object? Payload { get; }
}
