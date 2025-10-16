using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine.IO;
using System.Reflection;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Orchestration;
using Tests.Support;

namespace Osm.Cli.Tests.Commands;

public class CommandConsoleTests
{
    [Fact]
    public void WriteErrors_WritesCodeAndMessagePairs()
    {
        var console = new TestConsole();
        var errors = new[]
        {
            ValidationError.Create("cli.option.missing", "First validation failure."),
            ValidationError.Create("cli.option.invalid", "Second validation failure."),
        };

        CommandConsole.WriteErrors(console, errors);

        var expected = string.Join(Environment.NewLine, new[]
        {
            $"{errors[0].Code}: {errors[0].Message}",
            $"{errors[1].Code}: {errors[1].Message}",
        }) + Environment.NewLine;

        Assert.Equal(expected, console.Error!.ToString());
    }

    [Fact]
    public void EmitPipelineWarnings_PrefixesWarningsAndPreservesWhitespacePrefixedMessages()
    {
        var console = new TestConsole();
        var warnings = ImmutableArray.Create(
            "Profiler evidence missing for UNIQUE check.",
            "  - Consider re-running profiling with extended sampling.",
            string.Empty,
            "   ");

        CommandConsole.EmitPipelineWarnings(console, warnings);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "[warning] Profiler evidence missing for UNIQUE check.",
            "  - Consider re-running profiling with extended sampling.",
        }) + Environment.NewLine;

        Assert.Equal(expected, console.Error!.ToString());
    }

    [Fact]
    public void EmitPipelineLog_GroupsDuplicatesAndEmitsSamples()
    {
        var console = new TestConsole();
        var baseTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            new PipelineLogEntry(
                baseTimestamp,
                "Init",
                "Unique message",
                new Dictionary<string, string?>
                {
                    ["Module"] = "Sales",
                }),
            new PipelineLogEntry(
                baseTimestamp.AddMinutes(5),
                "Stage",
                "Duplicate message",
                new Dictionary<string, string?>
                {
                    ["Entity"] = "Customer",
                    ["Status"] = null,
                }),
            new PipelineLogEntry(
                baseTimestamp.AddMinutes(10),
                "Stage",
                "Duplicate message",
                new Dictionary<string, string?>()),
            new PipelineLogEntry(
                baseTimestamp.AddMinutes(15),
                "Stage",
                "Duplicate message",
                new Dictionary<string, string?>
                {
                    ["Sample"] = "One",
                }),
            new PipelineLogEntry(
                baseTimestamp.AddMinutes(20),
                "Stage",
                "Duplicate message",
                new Dictionary<string, string?>
                {
                    ["Extra"] = "Ignored",
                }),
        };

        var log = CreateExecutionLog(entries);

        CommandConsole.EmitPipelineLog(console, log);

        var lines = console.Out!.ToString()!
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(8, lines.Length);
        Assert.Equal("Pipeline execution log:", lines[0]);
        Assert.Equal($"[{baseTimestamp:O}] Init: Unique message | Module=Sales", lines[1]);

        var firstDuplicateTimestamp = entries[1].TimestampUtc;
        var lastDuplicateTimestamp = entries[^1].TimestampUtc;
        Assert.Equal($"[Stage] Duplicate message \u2013 4 occurrence(s) between {firstDuplicateTimestamp:O} and {lastDuplicateTimestamp:O}.", lines[2]);
        Assert.Equal("  Examples:", lines[3]);
        Assert.Equal($"    [{entries[1].TimestampUtc:O}] Entity=Customer, Status=<null>", lines[4]);
        Assert.Equal($"    [{entries[2].TimestampUtc:O}] (no metadata)", lines[5]);
        Assert.Equal($"    [{entries[3].TimestampUtc:O}] Sample=One", lines[6]);
        Assert.Equal("    \u2026 1 additional occurrence(s) suppressed.", lines[7]);
    }

    [Fact]
    public void EmitSqlProfilerSnapshot_WritesHeaderAndFormatterOutput()
    {
        var console = new TestConsole();
        var snapshot = ProfileFixtures.LoadSnapshot("profiling/profile.micro-unique.json");
        var expectedJson = ProfileSnapshotDebugFormatter.ToJson(snapshot);

        CommandConsole.EmitSqlProfilerSnapshot(console, snapshot);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "SQL profiler snapshot:",
            expectedJson,
        }) + Environment.NewLine;

        Assert.Equal(expected, console.Out!.ToString());
    }

    private static PipelineExecutionLog CreateExecutionLog(IReadOnlyList<PipelineLogEntry> entries)
    {
        var constructor = typeof(PipelineExecutionLog).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(IReadOnlyList<PipelineLogEntry>) },
            modifiers: null);

        if (constructor is null)
        {
            throw new InvalidOperationException("Expected PipelineExecutionLog constructor not found.");
        }

        return (PipelineExecutionLog)constructor.Invoke(new object[] { entries });
    }
}
