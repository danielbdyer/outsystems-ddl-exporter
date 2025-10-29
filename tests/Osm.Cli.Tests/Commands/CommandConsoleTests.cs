using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
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
    public void EmitToggleSnapshot_WritesValuesWithSources()
    {
        var console = new TestConsole();
        var snapshot = new TighteningToggleSnapshot(
            new ToggleState<TighteningMode>(TighteningMode.Aggressive, ToggleSource.CommandLine),
            new ToggleState<double>(0.25, ToggleSource.Environment),
            new ToggleState<bool>(true, ToggleSource.Configuration),
            new ToggleState<bool>(false, ToggleSource.Default),
            new ToggleState<bool>(true, ToggleSource.Environment),
            new ToggleState<bool>(false, ToggleSource.Configuration),
            new ToggleState<bool>(true, ToggleSource.Default),
            new ToggleState<bool>(false, ToggleSource.CommandLine),
            new ToggleState<bool>(true, ToggleSource.Configuration),
            new ToggleState<int>(123, ToggleSource.Environment));
        var export = snapshot.ToExportDictionary();

        CommandConsole.EmitToggleSnapshot(console, export);

        var lines = console.Out!.ToString()!
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.True(lines.Length > 1);
        Assert.Equal("Tightening toggles:", lines[0]);

        var table = lines
            .Skip(1)
            .Select(line => line.Trim())
            .Select(line =>
            {
                var parts = line.Split('→', 2);
                Assert.Equal(2, parts.Length);
                return (Key: parts[0].Trim(), Value: parts[1].Trim());
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TighteningToggleKeys.PolicyMode] = "Aggressive (command-line)",
            [TighteningToggleKeys.PolicyNullBudget] = "0.25 (environment)",
            [TighteningToggleKeys.ForeignKeysEnableCreation] = "true (configuration)",
            [TighteningToggleKeys.ForeignKeysAllowCrossSchema] = "false (default)",
            [TighteningToggleKeys.ForeignKeysAllowCrossCatalog] = "true (environment)",
            [TighteningToggleKeys.ForeignKeysTreatMissingDeleteRuleAsIgnore] = "false (configuration)",
            [TighteningToggleKeys.UniquenessEnforceSingleColumn] = "true (default)",
            [TighteningToggleKeys.UniquenessEnforceMultiColumn] = "false (command-line)",
            [TighteningToggleKeys.RemediationGeneratePreScripts] = "true (configuration)",
            [TighteningToggleKeys.RemediationMaxRowsDefaultBackfill] = "123 (environment)",
        };

        Assert.Equal(expected.Count, table.Count);

        foreach (var pair in expected)
        {
            Assert.True(table.TryGetValue(pair.Key, out var actual), $"Missing toggle output for {pair.Key}");
            Assert.Equal(pair.Value, actual);
        }
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
