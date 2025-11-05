using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Orchestration;

namespace Osm.Json;

public interface IPipelineExecutionLogSerializer
{
    Task SerializeAsync(PipelineExecutionLog log, Stream destination, CancellationToken cancellationToken = default);
}

public sealed class PipelineExecutionLogSerializer : IPipelineExecutionLogSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public async Task SerializeAsync(PipelineExecutionLog log, Stream destination, CancellationToken cancellationToken = default)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var document = new PipelineExecutionLogDocument
        {
            Entries = log.Entries.Select(MapEntry).ToArray()
        };

        await JsonSerializer.SerializeAsync(destination, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static PipelineLogEntryDocument MapEntry(PipelineLogEntry entry)
    {
        return new PipelineLogEntryDocument
        {
            TimestampUtc = entry.TimestampUtc,
            Step = entry.Step,
            Message = entry.Message,
            Metadata = entry.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    private sealed record PipelineExecutionLogDocument
    {
        [JsonPropertyName("entries")]
        public PipelineLogEntryDocument[] Entries { get; init; } = Array.Empty<PipelineLogEntryDocument>();
    }

    private sealed record PipelineLogEntryDocument
    {
        [JsonPropertyName("timestampUtc")]
        public DateTimeOffset TimestampUtc { get; init; }

        [JsonPropertyName("step")]
        public string Step { get; init; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("metadata")]
        public System.Collections.Generic.Dictionary<string, string?> Metadata { get; init; } = new();
    }
}
