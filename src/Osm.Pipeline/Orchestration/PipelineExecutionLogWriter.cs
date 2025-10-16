using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Orchestration;

public sealed class PipelineExecutionLogWriter
{
    public async Task<string> WriteAsync(
        string outputDirectory,
        PipelineExecutionLog log,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        var path = Path.Combine(outputDirectory, "pipeline-log.json");
        Directory.CreateDirectory(outputDirectory);

        var export = new PipelineExecutionLogExport(
            log.Entries.Select(entry => new PipelineExecutionLogExportEntry(
                entry.TimestampUtc,
                entry.Step,
                entry.Message,
                entry.Metadata.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value,
                    StringComparer.Ordinal))).ToArray());

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private sealed record PipelineExecutionLogExport(
        IReadOnlyList<PipelineExecutionLogExportEntry> Entries);

    private sealed record PipelineExecutionLogExportEntry(
        DateTimeOffset TimestampUtc,
        string Step,
        string Message,
        IReadOnlyDictionary<string, string?> Metadata);
}
