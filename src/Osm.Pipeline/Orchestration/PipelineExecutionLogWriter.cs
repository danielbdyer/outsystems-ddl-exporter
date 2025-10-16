using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Orchestration;

public sealed class PipelineExecutionLogWriter
{
    public const string LogFileName = "pipeline-log.json";
    public const string WarningsFileName = "pipeline-warnings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public async Task<PipelineExecutionLogWriterResult> WriteAsync(
        string outputDirectory,
        PipelineExecutionLog executionLog,
        ImmutableArray<string> warnings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (executionLog is null)
        {
            throw new ArgumentNullException(nameof(executionLog));
        }

        Directory.CreateDirectory(outputDirectory);

        var logPayload = new PipelineExecutionLogPayload(
            executionLog.Entries.Select(static entry => new PipelineExecutionLogEntryPayload(
                entry.TimestampUtc,
                entry.Step,
                entry.Message,
                entry.Metadata)).ToArray());

        var logPath = Path.Combine(outputDirectory, LogFileName);
        await File.WriteAllTextAsync(
                logPath,
                JsonSerializer.Serialize(logPayload, SerializerOptions),
                cancellationToken)
            .ConfigureAwait(false);

        var warningsPayload = warnings.IsDefault ? Array.Empty<string>() : warnings.ToArray();
        var warningsPath = Path.Combine(outputDirectory, WarningsFileName);
        await File.WriteAllTextAsync(
                warningsPath,
                JsonSerializer.Serialize(warningsPayload, SerializerOptions),
                cancellationToken)
            .ConfigureAwait(false);

        return new PipelineExecutionLogWriterResult(logPath, warningsPath);
    }

    private sealed record PipelineExecutionLogPayload(
        IReadOnlyList<PipelineExecutionLogEntryPayload> Entries);

    private sealed record PipelineExecutionLogEntryPayload(
        DateTimeOffset TimestampUtc,
        string Step,
        string Message,
        IReadOnlyDictionary<string, string?> Metadata);
}

public sealed record PipelineExecutionLogWriterResult(string LogPath, string WarningsPath);
