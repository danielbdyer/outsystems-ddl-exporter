using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine.IO;
using System.Reflection;
using System.Text.Json;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

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
    public void EmitPipelineInsights_WritesInfoInsightsToStandardOutput()
    {
        var console = new TestConsole();
        var insight = new PipelineInsight(
            "pipeline.insight.info",
            "All clear",
            "No evidence of drift detected.",
            PipelineInsightSeverity.Info,
            ImmutableArray<string>.Empty,
            "None");

        CommandConsole.EmitPipelineInsights(console, ImmutableArray.Create(insight));

        Assert.Contains("ℹ️ No evidence of drift detected.", console.Out!.ToString());
        Assert.True(string.IsNullOrEmpty(console.Error!.ToString()));
    }

    [Fact]
    public void EmitPipelineInsights_WritesWarningsToStandardError()
    {
        var console = new TestConsole();
        var insight = new PipelineInsight(
            "pipeline.insight.warning",
            "Potential drift",
            "Potential data drift detected for Sales module.",
            PipelineInsightSeverity.Warning,
            ImmutableArray<string>.Empty,
            "Investigate the profiling job.");

        CommandConsole.EmitPipelineInsights(console, ImmutableArray.Create(insight));

        Assert.Contains("⚠️ Potential data drift detected for Sales module.", console.Error!.ToString());
        Assert.True(string.IsNullOrEmpty(console.Out!.ToString()));
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

    [Fact]
    public void EmitNamingOverrideTemplate_WritesTemplateWhenDuplicatesExist()
    {
        var console = new TestConsole();
        var candidates = ImmutableArray.Create(
            new TighteningDuplicateCandidate("Sales", "dbo", "OSUSR_SAL_ENTITY"),
            new TighteningDuplicateCandidate("Support", "dbo", "OSUSR_SUP_ENTITY"));
        var diagnostics = ImmutableArray.Create(
            new TighteningDiagnostic(
                "tightening.entity.duplicate.unresolved",
                "Duplicate logical name detected.",
                TighteningDiagnosticSeverity.Warning,
                "EntityType",
                "Sales",
                "dbo",
                "OSUSR_SAL_ENTITY",
                candidates,
                ResolvedByOverride: false));

        CommandConsole.EmitNamingOverrideTemplate(console, diagnostics);

        var errorLines = console.Error!.ToString()!.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(errorLines);
        Assert.Equal("[action] Duplicate logical entity names detected. Use the template below to populate emission.namingOverrides.rules:", errorLines[0]);

        var json = console.Out!.ToString()!;
        Assert.False(string.IsNullOrWhiteSpace(json));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var emission = root.GetProperty("emission");
        var namingOverrides = emission.GetProperty("namingOverrides");
        var rules = namingOverrides.GetProperty("rules");
        Assert.Equal(2, rules.GetArrayLength());

        var first = rules[0];
        Assert.Equal("dbo", first.GetProperty("schema").GetString());
        Assert.Equal("OSUSR_SAL_ENTITY", first.GetProperty("table").GetString());
        Assert.Equal("Sales", first.GetProperty("module").GetString());
        Assert.Equal("EntityType", first.GetProperty("entity").GetString());
        Assert.Equal("Sales_EntityType", first.GetProperty("override").GetString());

        var second = rules[1];
        Assert.Equal("Support", second.GetProperty("module").GetString());
        Assert.Equal("Support_EntityType", second.GetProperty("override").GetString());
    }

    [Fact]
    public void EmitNamingOverrideTemplate_SuppressesOutputWhenNoDuplicates()
    {
        var console = new TestConsole();
        var diagnostics = ImmutableArray.Create(
            new TighteningDiagnostic(
                "tightening.entity.unique",
                "No duplicates present.",
                TighteningDiagnosticSeverity.Info,
                "EntityType",
                "Sales",
                "dbo",
                "OSUSR_SAL_ENTITY",
                ImmutableArray<TighteningDuplicateCandidate>.Empty,
                ResolvedByOverride: false));

        CommandConsole.EmitNamingOverrideTemplate(console, diagnostics);

        Assert.True(string.IsNullOrEmpty(console.Out!.ToString()));
        Assert.True(string.IsNullOrEmpty(console.Error!.ToString()));
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
