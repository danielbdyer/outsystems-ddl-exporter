using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.IO;
using System.Linq;
using Osm.Cli;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal static class CommandConsole
{
    public static void WriteLine(IConsole console, string message)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        console.Out.Write(message + Environment.NewLine);
    }

    public static void WriteErrorLine(IConsole console, string message)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        console.Error.Write(message + Environment.NewLine);
    }

    public static void WriteErrors(IConsole console, IEnumerable<ValidationError> errors)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        foreach (var error in errors)
        {
            WriteErrorLine(console, $"{error.Code}: {error.Message}");
        }
    }

    public static void EmitPipelineWarnings(IConsole console, ImmutableArray<string> warnings)
    {
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

            if (char.IsWhiteSpace(warning[0]))
            {
                WriteErrorLine(console, warning);
            }
            else
            {
                WriteErrorLine(console, $"[warning] {warning}");
            }
        }
    }

    public static void EmitSqlProfilerSnapshot(
        IConsole console,
        ProfileInsightReport insightReport,
        ProfileSnapshot snapshot,
        bool emitJson = false,
        string? jsonOutputPath = null)
    {
        if (insightReport is null)
        {
            throw new ArgumentNullException(nameof(insightReport));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        WriteLine(console, "SQL profiler snapshot:");
        foreach (var line in ProfileSnapshotDebugFormatter.ToSummaryLines(insightReport))
        {
            WriteLine(console, line);
        }

        string? json = null;

        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
        {
            json ??= ProfileSnapshotDebugFormatter.ToJson(snapshot);

            try
            {
                var directory = Path.GetDirectoryName(jsonOutputPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                File.WriteAllText(jsonOutputPath!, json);
                WriteLine(console, $"Raw profiler snapshot written to {jsonOutputPath}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                WriteErrorLine(console, $"[warning] Failed to write profiler snapshot to {jsonOutputPath}: {ex.Message}");
            }
        }

        if (emitJson)
        {
            json ??= ProfileSnapshotDebugFormatter.ToJson(snapshot);
            WriteLine(console, string.Empty);
            WriteLine(console, json);
        }
    }

    public static void EmitPipelineLog(IConsole console, PipelineExecutionLog log)
    {
        if (log is null || log.Entries.Count == 0)
        {
            return;
        }

        WriteLine(console, "Pipeline execution log:");
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
                WriteLine(console, FormatLogEntry(entries[0]));
                continue;
            }

            var first = entries[0];
            var last = entries[^1];
            WriteLine(console, $"[{first.Step}] {first.Message} – {entries.Count} occurrence(s) between {first.TimestampUtc:O} and {last.TimestampUtc:O}.");

            var sampleCount = Math.Min(3, entries.Count);
            WriteLine(console, "  Examples:");
            for (var i = 0; i < sampleCount; i++)
            {
                WriteLine(console, $"    {FormatLogSample(entries[i])}");
            }

            if (entries.Count > sampleCount)
            {
                WriteLine(console, $"    … {entries.Count - sampleCount} additional occurrence(s) suppressed.");
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
