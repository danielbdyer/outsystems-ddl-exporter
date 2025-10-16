using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Hosting;

internal static class VerbLogging
{
    public static void LogErrors(ILogger logger, IReadOnlyCollection<ValidationError> errors)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        foreach (var error in errors)
        {
            logger.LogError("{Code}: {Message}", error.Code, error.Message);
        }
    }

    public static void LogWarnings(ILogger logger, ImmutableArray<string> warnings)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        if (warnings.IsDefaultOrEmpty || warnings.Length == 0)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                continue;
            }

            logger.LogWarning("{Warning}", warning);
        }
    }

    public static void LogPipelineLog(ILogger logger, PipelineExecutionLog log)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        if (log is null || log.Entries.Count == 0)
        {
            return;
        }

        var order = new List<(string Step, string Message)>();
        var grouped = new Dictionary<(string Step, string Message), List<PipelineLogEntry>>();

        foreach (var entry in log.Entries)
        {
            var key = (entry.Step, entry.Message);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = new List<PipelineLogEntry>();
                grouped[key] = list;
                order.Add(key);
            }

            list.Add(entry);
        }

        foreach (var key in order)
        {
            var entries = grouped[key];
            if (entries.Count == 1)
            {
                logger.LogInformation("{Entry}", FormatLogEntry(entries[0]));
                continue;
            }

            var first = entries[0];
            var last = entries[^1];
            logger.LogInformation(
                "[{Step}] {Message} – {Count} occurrence(s) between {First} and {Last}.",
                first.Step,
                first.Message,
                entries.Count,
                first.TimestampUtc.ToString("O"),
                last.TimestampUtc.ToString("O"));

            var sampleCount = Math.Min(3, entries.Count);
            for (var i = 0; i < sampleCount; i++)
            {
                logger.LogInformation("    {Sample}", FormatLogSample(entries[i]));
            }

            if (entries.Count > sampleCount)
            {
                logger.LogInformation("    … {Additional} additional occurrence(s) suppressed.", entries.Count - sampleCount);
            }
        }
    }

    private static string FormatLogEntry(PipelineLogEntry entry)
    {
        var metadata = entry.Metadata.Count == 0
            ? string.Empty
            : " | " + string.Join(", ", entry.Metadata.Select(pair => $"{pair.Key}={FormatMetadataValue(pair.Value)}"));

        return $"[{entry.TimestampUtc:O}] {entry.Step}: {entry.Message}{metadata}";
    }

    private static string FormatLogSample(PipelineLogEntry entry)
    {
        if (entry.Metadata.Count == 0)
        {
            return $"[{entry.TimestampUtc:O}] (no metadata)";
        }

        var metadata = string.Join(", ", entry.Metadata.Select(pair => $"{pair.Key}={FormatMetadataValue(pair.Value)}"));
        return $"[{entry.TimestampUtc:O}] {metadata}";
    }

    private static string FormatMetadataValue(string? value)
        => value ?? "<null>";
}
