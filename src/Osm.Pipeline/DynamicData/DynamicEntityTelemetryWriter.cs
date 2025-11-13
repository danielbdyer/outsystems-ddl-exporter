using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.DynamicData;

internal static class DynamicEntityTelemetryWriter
{
    private const string TelemetryFileName = "dynamic-data.telemetry.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<string> WriteAsync(
        string outputDirectory,
        DynamicEntityExtractionTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (telemetry is null)
        {
            throw new ArgumentNullException(nameof(telemetry));
        }

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, TelemetryFileName);

        var payload = new
        {
            startedAtUtc = telemetry.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            completedAtUtc = telemetry.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            durationMs = (telemetry.CompletedAtUtc - telemetry.StartedAtUtc).TotalMilliseconds,
            tableCount = telemetry.TableCount,
            rowCount = telemetry.RowCount,
            tables = telemetry.Tables.Select(table => new
            {
                module = table.Module,
                entity = table.Entity,
                schema = table.Schema,
                physicalName = table.PhysicalName,
                effectiveName = table.EffectiveName,
                rowCount = table.RowCount,
                batchCount = table.BatchCount,
                durationMs = table.Duration.TotalMilliseconds,
                checksum = table.Checksum,
                chunks = table.Chunks.Select(chunk => new
                {
                    sequence = chunk.Sequence,
                    rowCount = chunk.RowCount,
                    durationMs = chunk.Duration.TotalMilliseconds
                }).ToArray()
            }).ToArray()
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return path;
    }
}
