using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Cli;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Application;
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

    public static void EmitSqlProfilerSnapshot(IConsole console, ProfileSnapshot snapshot)
    {
        WriteLine(console, "SQL profiler snapshot:");
        WriteLine(console, ProfileSnapshotDebugFormatter.ToJson(snapshot));
    }

    public static void EmitBuildSsdtJson(
        IConsole console,
        BuildSsdtApplicationResult applicationResult,
        ImmutableArray<string> pipelineWarnings,
        string? reportPath,
        string? reportError)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (applicationResult is null)
        {
            throw new ArgumentNullException(nameof(applicationResult));
        }

        var pipelineResult = applicationResult.PipelineResult ?? throw new ArgumentException("Pipeline result missing.", nameof(applicationResult));

        var payload = new BuildSsdtConsolePayload(
            Command: "build-ssdt",
            Model: new BuildSsdtConsoleModel(
                applicationResult.ModelPath,
                applicationResult.ModelWasExtracted,
                Normalize(applicationResult.ModelExtractionWarnings)),
            Profile: new BuildSsdtConsoleProfile(
                applicationResult.ProfilerProvider,
                applicationResult.ProfilePath),
            Output: new BuildSsdtConsoleOutput(
                applicationResult.OutputDirectory,
                Path.Combine(applicationResult.OutputDirectory, "manifest.json"),
                pipelineResult.DecisionLogPath,
                Normalize(pipelineResult.StaticSeedScriptPaths)),
            Telemetry: new BuildSsdtConsoleTelemetry(
                pipelineResult.Manifest.Tables.Count,
                pipelineResult.DecisionReport.TightenedColumnCount,
                pipelineResult.DecisionReport.ColumnCount,
                pipelineResult.DecisionReport.UniqueIndexesEnforcedCount,
                pipelineResult.DecisionReport.UniqueIndexCount,
                pipelineResult.DecisionReport.ForeignKeysCreatedCount,
                pipelineResult.DecisionReport.ForeignKeyCount),
            Warnings: BuildWarnings(pipelineWarnings, reportError),
            Diagnostics: pipelineResult.DecisionReport.Diagnostics
                .Where(d => d.Severity == TighteningDiagnosticSeverity.Warning)
                .Select(d => new BuildSsdtConsoleDiagnostic(
                    d.Code,
                    d.Message,
                    d.LogicalName,
                    d.CanonicalModule,
                    d.CanonicalSchema,
                    d.CanonicalPhysicalName,
                    d.ResolvedByOverride))
                .ToArray(),
            EvidenceCache: pipelineResult.EvidenceCache is { } cache
                ? new BuildSsdtConsoleEvidenceCache(
                    cache.CacheDirectory,
                    cache.Manifest.Key,
                    Path.Combine(cache.CacheDirectory, "manifest.json"),
                    cache.Evaluation.Outcome.ToString(),
                    cache.Evaluation.Reason.ToString())
                : null,
            Report: BuildReport(reportPath, reportError));

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        WriteLine(console, json);
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

    private static string[] Normalize(ImmutableArray<string> values)
        => values.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

    private static string[] BuildWarnings(ImmutableArray<string> pipelineWarnings, string? reportError)
    {
        var warnings = Normalize(pipelineWarnings).ToList();
        if (!string.IsNullOrWhiteSpace(reportError))
        {
            warnings.Add(reportError);
        }

        return warnings.Count == 0 ? Array.Empty<string>() : warnings.ToArray();
    }

    private static BuildSsdtConsoleReport? BuildReport(string? reportPath, string? reportError)
    {
        if (string.IsNullOrWhiteSpace(reportPath) && string.IsNullOrWhiteSpace(reportError))
        {
            return null;
        }

        return new BuildSsdtConsoleReport(reportPath, reportError);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private sealed record BuildSsdtConsolePayload(
        string Command,
        BuildSsdtConsoleModel Model,
        BuildSsdtConsoleProfile Profile,
        BuildSsdtConsoleOutput Output,
        BuildSsdtConsoleTelemetry Telemetry,
        string[] Warnings,
        BuildSsdtConsoleDiagnostic[] Diagnostics,
        BuildSsdtConsoleEvidenceCache? EvidenceCache,
        BuildSsdtConsoleReport? Report);

    private sealed record BuildSsdtConsoleModel(string Path, bool WasExtracted, string[] Warnings);

    private sealed record BuildSsdtConsoleProfile(string Provider, string? Path);

    private sealed record BuildSsdtConsoleOutput(
        string Directory,
        string ManifestPath,
        string PolicyDecisionsPath,
        string[] StaticSeedScripts);

    private sealed record BuildSsdtConsoleTelemetry(
        int TablesEmitted,
        int ColumnsTightened,
        int ColumnCount,
        int UniqueIndexesEnforced,
        int UniqueIndexCount,
        int ForeignKeysCreated,
        int ForeignKeyCount);

    private sealed record BuildSsdtConsoleDiagnostic(
        string Code,
        string Message,
        string LogicalName,
        string Module,
        string Schema,
        string PhysicalName,
        bool ResolvedByOverride);

    private sealed record BuildSsdtConsoleEvidenceCache(
        string Directory,
        string Key,
        string ManifestPath,
        string Outcome,
        string InvalidationReason);

    private sealed record BuildSsdtConsoleReport(string? Path, string? Error);
}
