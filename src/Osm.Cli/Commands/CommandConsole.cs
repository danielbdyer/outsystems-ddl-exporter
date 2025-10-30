using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Cli;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal static class CommandConsole
{
    private static readonly JsonSerializerOptions NamingOverrideSerializerOptions = new()
    {
        WriteIndented = true
    };

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
            var metadataSuffix = error.HasMetadata
                ? " | " + string.Join(", ", error.Metadata.Select(pair => $"{pair.Key}={FormatMetadataValue(pair.Value)}"))
                : string.Empty;
            WriteErrorLine(console, $"{error.Code}: {error.Message}{metadataSuffix}");
        }
    }

    public static void WriteTable(IConsole console, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (headers is null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        if (headers.Count == 0)
        {
            return;
        }

        var columnCount = headers.Count;
        var widths = new int[columnCount];

        for (var i = 0; i < columnCount; i++)
        {
            widths[i] = headers[i]?.Length ?? 0;
        }

        if (rows is not null)
        {
            foreach (var row in rows)
            {
                if (row is null)
                {
                    continue;
                }

                for (var i = 0; i < columnCount && i < row.Count; i++)
                {
                    var valueLength = (row[i] ?? string.Empty).Length;
                    if (valueLength > widths[i])
                    {
                        widths[i] = valueLength;
                    }
                }
            }
        }

        WriteLine(console, BuildTableRow(headers, widths));
        WriteLine(console, BuildSeparator(widths));

        if (rows is null || rows.Count == 0)
        {
            WriteLine(console, "(no entries)");
            return;
        }

        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var values = new string[columnCount];
            for (var i = 0; i < columnCount; i++)
            {
                values[i] = i < row.Count ? row[i] ?? string.Empty : string.Empty;
            }

            WriteLine(console, BuildTableRow(values, widths));
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

    public static void EmitProfilingInsights(IConsole console, ImmutableArray<ProfilingInsight> insights)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (insights.IsDefaultOrEmpty || insights.Length == 0)
        {
            return;
        }

        WriteLine(console, "Profiling insights:");

        foreach (var insight in insights)
        {
            if (insight is null)
            {
                continue;
            }

            var formatted = FormatProfilingInsight(insight);
            switch (insight.Severity)
            {
                case ProfilingInsightSeverity.Warning:
                case ProfilingInsightSeverity.Error:
                    WriteErrorLine(console, formatted);
                    break;
                default:
                    WriteLine(console, formatted);
                    break;
            }
        }
    }

    public static void EmitProfilerPreflightDiagnostics(IConsole console, ImmutableArray<SqlProfilerPreflightDiagnostic> diagnostics)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (diagnostics.IsDefaultOrEmpty || diagnostics.Length == 0)
        {
            return;
        }

        WriteLine(console, "Profiler preflight:");

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null || string.IsNullOrWhiteSpace(diagnostic.Message))
            {
                continue;
            }

            var prefix = diagnostic.Severity switch
            {
                SqlProfilerPreflightSeverity.Warning => "[warning] ",
                _ => "[info] "
            };

            if (diagnostic.Severity == SqlProfilerPreflightSeverity.Warning)
            {
                WriteErrorLine(console, prefix + diagnostic.Message);
            }
            else
            {
                WriteLine(console, prefix + diagnostic.Message);
            }
        }
    }

    public static void EmitModuleRollups(
        IConsole console,
        ImmutableDictionary<string, ModuleManifestRollup> manifestRollups,
        ImmutableDictionary<string, ModuleDecisionRollup> decisionRollups)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        manifestRollups ??= ImmutableDictionary<string, ModuleManifestRollup>.Empty;
        decisionRollups ??= ImmutableDictionary<string, ModuleDecisionRollup>.Empty;

        if (manifestRollups.Count == 0 && decisionRollups.Count == 0)
        {
            return;
        }

        var modules = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        modules.UnionWith(manifestRollups.Keys);
        modules.UnionWith(decisionRollups.Keys);

        if (modules.Count == 0)
        {
            return;
        }

        WriteLine(console, "Module rollups:");
        foreach (var module in modules)
        {
            if (!manifestRollups.TryGetValue(module, out var manifest))
            {
                manifest = ModuleManifestRollup.Empty;
            }

            decisionRollups.TryGetValue(module, out var decision);

            var parts = new[]
            {
                $"tables={manifest.TableCount:N0}",
                $"indexes={manifest.IndexCount:N0}",
                $"foreignKeys={manifest.ForeignKeyCount:N0}",
                $"columns={decision?.ColumnCount ?? 0:N0}",
                $"tightened={decision?.TightenedColumnCount ?? 0:N0}",
                $"remediation={decision?.RemediationColumnCount ?? 0:N0}",
                $"uniqueEnforced={decision?.UniqueIndexesEnforcedCount ?? 0:N0}",
                $"uniqueRemediation={decision?.UniqueIndexesRequireRemediationCount ?? 0:N0}",
                $"foreignKeysCreated={decision?.ForeignKeysCreatedCount ?? 0:N0}"
            };

            WriteLine(console, $"  {module}: {string.Join(", ", parts)}");
        }
    }

    public static void EmitTogglePrecedence(
        IConsole console,
        IReadOnlyDictionary<string, ToggleExportValue> togglePrecedence)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (togglePrecedence is null || togglePrecedence.Count == 0)
        {
            return;
        }

        WriteLine(console, "Tightening toggles:");

        foreach (var pair in togglePrecedence.OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var formattedValue = FormatToggleValue(pair.Value.Value);
            WriteLine(console, $"  {pair.Key} = {formattedValue} ({pair.Value.Source})");
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

    private static string FormatToggleValue(object? value)
        => value switch
        {
            null => "<null>",
            bool boolean => boolean ? "true" : "false",
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static string FormatProfilingInsight(ProfilingInsight insight)
    {
        var severityText = insight.Severity.ToString().ToLowerInvariant();
        var coordinateText = FormatProfilingInsightCoordinate(insight.Coordinate);

        if (string.IsNullOrWhiteSpace(coordinateText))
        {
            return $"[{severityText}] {insight.Message}";
        }

        return $"[{severityText}] {coordinateText}: {insight.Message}";
    }

    private static string FormatProfilingInsightCoordinate(ProfilingInsightCoordinate? coordinate)
    {
        if (coordinate is null)
        {
            return string.Empty;
        }

        var primary = BuildCoordinateSegment(coordinate.Schema.Value, coordinate.Table.Value, coordinate.Column?.Value);

        if (coordinate.RelatedSchema is null || coordinate.RelatedTable is null)
        {
            return primary;
        }

        var related = BuildCoordinateSegment(
            coordinate.RelatedSchema.Value.Value,
            coordinate.RelatedTable.Value.Value,
            coordinate.RelatedColumn?.Value);

        if (string.IsNullOrWhiteSpace(related))
        {
            return primary;
        }

        return $"{primary} -> {related}";
    }

    private static string BuildCoordinateSegment(string schema, string table, string? column)
    {
        var segment = $"{schema}.{table}";

        if (!string.IsNullOrWhiteSpace(column))
        {
            segment += $".{column}";
        }

        return segment;
    }

    private static string BuildTableRow(IReadOnlyList<string> values, int[] widths)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("  ");
            }

            var value = values[i] ?? string.Empty;
            builder.Append(value.PadRight(widths[i]));
        }

        return builder.ToString();
    }

    private static string BuildSeparator(int[] widths)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < widths.Length; i++)
        {
            if (i > 0)
            {
                builder.Append("  ");
            }

            var dashCount = Math.Max(3, widths[i]);
            builder.Append(new string('-', dashCount));
        }

        return builder.ToString();
    }

    public static void EmitNamingOverrideTemplate(
        IConsole console,
        IEnumerable<TighteningDiagnostic> diagnostics)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (diagnostics is null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        if (!TryBuildNamingOverrideTemplate(diagnostics, out var templateJson))
        {
            return;
        }

        WriteErrorLine(console, "[action] Duplicate logical entity names detected. Use the template below to populate emission.namingOverrides.rules:");
        WriteLine(console, templateJson);
    }

    private static bool TryBuildNamingOverrideTemplate(
        IEnumerable<TighteningDiagnostic> diagnostics,
        out string templateJson)
    {
        var rules = new List<NamingOverrideTemplateRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                continue;
            }

            if (!diagnostic.Code.StartsWith("tightening.entity.duplicate", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var candidate in diagnostic.Candidates)
            {
                var key = string.Join(
                    "|",
                    candidate.Schema,
                    candidate.PhysicalName,
                    candidate.Module,
                    diagnostic.LogicalName);

                if (!seen.Add(key))
                {
                    continue;
                }

                rules.Add(new NamingOverrideTemplateRule(
                    candidate.Schema,
                    candidate.PhysicalName,
                    candidate.Module,
                    diagnostic.LogicalName,
                    $"{candidate.Module}_{diagnostic.LogicalName}"));
            }
        }

        if (rules.Count == 0)
        {
            templateJson = string.Empty;
            return false;
        }

        var template = new
        {
            emission = new
            {
                namingOverrides = new
                {
                    rules
                }
            }
        };

        templateJson = JsonSerializer.Serialize(template, NamingOverrideSerializerOptions);
        return true;
    }

    private sealed record NamingOverrideTemplateRule(
        [property: JsonPropertyName("schema"), JsonPropertyOrder(0)] string Schema,
        [property: JsonPropertyName("table"), JsonPropertyOrder(1)] string Table,
        [property: JsonPropertyName("module"), JsonPropertyOrder(2)] string Module,
        [property: JsonPropertyName("entity"), JsonPropertyOrder(3)] string Entity,
        [property: JsonPropertyName("override"), JsonPropertyOrder(4)] string Override);
}
