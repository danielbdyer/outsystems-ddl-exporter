using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Dmm;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;

namespace Osm.Cli;

internal static class CliCommandUtilities
{
    public static IServiceProvider GetServices(InvocationContext context)
        => context.BindingContext.GetRequiredService<IServiceProvider>();

    public static void AddSqlOptions(Command command, SqlOptionSet optionSet)
    {
        command.AddOption(optionSet.ConnectionString);
        command.AddOption(optionSet.CommandTimeout);
        command.AddOption(optionSet.SamplingThreshold);
        command.AddOption(optionSet.SamplingSize);
        command.AddOption(optionSet.AuthenticationMethod);
        command.AddOption(optionSet.TrustServerCertificate);
        command.AddOption(optionSet.ApplicationName);
        command.AddOption(optionSet.AccessToken);
    }

    public static SqlOptionsOverrides CreateSqlOverrides(ParseResult parseResult, SqlOptionSet optionSet)
        => new(
            parseResult.GetValueForOption(optionSet.ConnectionString),
            parseResult.GetValueForOption(optionSet.CommandTimeout),
            parseResult.GetValueForOption(optionSet.SamplingThreshold),
            parseResult.GetValueForOption(optionSet.SamplingSize),
            parseResult.GetValueForOption(optionSet.AuthenticationMethod),
            parseResult.GetValueForOption(optionSet.TrustServerCertificate),
            parseResult.GetValueForOption(optionSet.ApplicationName),
            parseResult.GetValueForOption(optionSet.AccessToken));

    public static void WriteLine(IConsole console, string message)
        => console.Out.Write(message + Environment.NewLine);

    public static void WriteErrorLine(IConsole console, string message)
        => console.Error.Write(message + Environment.NewLine);

    public static IReadOnlyList<string> SplitModuleList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var separators = new[] { ',', ';' };
        return value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static IReadOnlyList<string> SplitOverrideList(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return Array.Empty<string>();
        }

        var separators = new[] { ',', ';' };
        var results = new List<string>();

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var tokens = value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    results.Add(token);
                }
            }
        }

        return results;
    }

    public static bool? ResolveIncludeOverride(InvocationContext context, Option<bool> includeOption, Option<bool> excludeOption)
    {
        if (context.ParseResult.HasOption(includeOption))
        {
            return true;
        }

        if (context.ParseResult.HasOption(excludeOption))
        {
            return false;
        }

        return null;
    }

    public static bool? ResolveInactiveOverride(InvocationContext context, Option<bool> includeInactiveOption, Option<bool> onlyActiveOption)
    {
        if (context.ParseResult.HasOption(includeInactiveOption))
        {
            return true;
        }

        if (context.ParseResult.HasOption(onlyActiveOption))
        {
            return false;
        }

        return null;
    }

    public static bool? ResolveOnlyActiveOverride(InvocationContext context, Option<bool> onlyActiveOption, Option<bool> includeInactiveOption)
    {
        if (context.ParseResult.HasOption(onlyActiveOption))
        {
            return true;
        }

        if (context.ParseResult.HasOption(includeInactiveOption))
        {
            return false;
        }

        return null;
    }

    public static bool IsSqlProfiler(string provider)
        => string.Equals(provider, "sql", StringComparison.OrdinalIgnoreCase);

    public static void EmitSqlProfilerSnapshot(InvocationContext context, ProfileSnapshot snapshot)
    {
        WriteLine(context.Console, "SQL profiler snapshot:");
        WriteLine(context.Console, ProfileSnapshotDebugFormatter.ToJson(snapshot));
    }

    public static void EmitPipelineLog(InvocationContext context, PipelineExecutionLog log)
    {
        if (log is null || log.Entries.Count == 0)
        {
            return;
        }

        WriteLine(context.Console, "Pipeline execution log:");
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
                WriteLine(context.Console, FormatLogEntry(entries[0]));
                continue;
            }

            var first = entries[0];
            var last = entries[^1];
            WriteLine(
                context.Console,
                $"[{first.Step}] {first.Message} – {entries.Count} occurrence(s) between {first.TimestampUtc:O} and {last.TimestampUtc:O}.");

            var sampleCount = Math.Min(3, entries.Count);
            WriteLine(context.Console, "  Examples:");
            for (var i = 0; i < sampleCount; i++)
            {
                WriteLine(context.Console, $"    {FormatLogSample(entries[i])}");
            }

            if (entries.Count > sampleCount)
            {
                WriteLine(context.Console, $"    … {entries.Count - sampleCount} additional occurrence(s) suppressed.");
            }
        }
    }

    public static void EmitPipelineWarnings(InvocationContext context, ImmutableArray<string> warnings)
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
                WriteErrorLine(context.Console, warning);
            }
            else
            {
                WriteErrorLine(context.Console, $"[warning] {warning}");
            }
        }
    }

    public static string FormatDifference(DmmDifference difference)
    {
        if (difference is null)
        {
            return string.Empty;
        }

        var scopeParts = new List<string>(capacity: 3);
        if (!string.IsNullOrWhiteSpace(difference.Schema))
        {
            scopeParts.Add(difference.Schema);
        }

        if (!string.IsNullOrWhiteSpace(difference.Table))
        {
            scopeParts.Add(difference.Table);
        }

        var scope = scopeParts.Count > 0 ? string.Join('.', scopeParts) : "artifact";

        if (!string.IsNullOrWhiteSpace(difference.Column))
        {
            scope += $".{difference.Column}";
        }
        else if (!string.IsNullOrWhiteSpace(difference.Index))
        {
            scope += $" [Index: {difference.Index}]";
        }
        else if (!string.IsNullOrWhiteSpace(difference.ForeignKey))
        {
            scope += $" [FK: {difference.ForeignKey}]";
        }

        var property = string.IsNullOrWhiteSpace(difference.Property) ? "Difference" : difference.Property;
        var expected = difference.Expected ?? "<none>";
        var actual = difference.Actual ?? "<none>";

        var message = $"{scope} – {property} expected {expected} actual {actual}";
        if (!string.IsNullOrWhiteSpace(difference.ArtifactPath))
        {
            message += $" ({difference.ArtifactPath})";
        }

        return message;
    }

    public static void WriteErrors(InvocationContext context, IEnumerable<ValidationError> errors)
    {
        foreach (var error in errors)
        {
            WriteErrorLine(context.Console, $"{error.Code}: {error.Message}");
        }
    }

    private static string FormatMetadataValue(string? value)
        => value ?? "<null>";

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
}
