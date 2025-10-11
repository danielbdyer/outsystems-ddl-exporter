using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Pipeline.Orchestration;

public sealed class PipelineExecutionLog
{
    private static readonly IReadOnlyList<PipelineLogEntry> EmptyEntries = Array.Empty<PipelineLogEntry>();

    internal PipelineExecutionLog(IReadOnlyList<PipelineLogEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    public IReadOnlyList<PipelineLogEntry> Entries { get; }

    public static PipelineExecutionLog Empty { get; } = new(EmptyEntries);
}

public sealed record PipelineLogEntry(
    DateTimeOffset TimestampUtc,
    string Step,
    string Message,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed class PipelineExecutionLogBuilder
{
    private readonly List<PipelineLogEntry> _entries = new();

    public void Record(
        string step,
        string message,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            throw new ArgumentException("Step must be provided.", nameof(step));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message must be provided.", nameof(message));
        }

        var entryMetadata = metadata is null || metadata.Count == 0
            ? ImmutableDictionary<string, string?>.Empty
            : metadata.ToImmutableDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);

        _entries.Add(new PipelineLogEntry(
            DateTimeOffset.UtcNow,
            step,
            message,
            entryMetadata));
    }

    public PipelineExecutionLog Build()
    {
        return new PipelineExecutionLog(_entries.ToArray());
    }
}
