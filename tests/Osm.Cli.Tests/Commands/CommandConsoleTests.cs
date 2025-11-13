using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine.IO;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tightening.Validations;

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
    public void EmitProfilingInsights_WritesStructuredSections()
    {
        var console = new TestConsole();

        var insights = ImmutableArray.Create(
            new ProfilingInsight(
                ProfilingInsightSeverity.Error,
                ProfilingInsightCategory.Nullability,
                "Nulls observed in NOT NULL column.",
                new ProfilingInsightCoordinate(
                    new SchemaName("dbo"),
                    new TableName("Orders"),
                    new ColumnName("CustomerId"),
                    null,
                    null,
                    null)),
            new ProfilingInsight(
                ProfilingInsightSeverity.Recommendation,
                ProfilingInsightCategory.ForeignKey,
                "Enable FK enforcement after remediation.",
                new ProfilingInsightCoordinate(
                    new SchemaName("dbo"),
                    new TableName("Orders"),
                    null,
                    new SchemaName("dbo"),
                    new TableName("Customers"),
                    null)),
            new ProfilingInsight(
                ProfilingInsightSeverity.Info,
                ProfilingInsightCategory.Evidence,
                "Sampling truncated after 10k rows.",
                null));

        CommandConsole.EmitProfilingInsights(console, insights);

        var output = console.Out!.ToString() ?? string.Empty;

        Assert.Contains("Profiling insights: 3 total (1 errors, 0 warnings, 2 informational)", output);
        Assert.Contains("High severity insights:", output);
        Assert.Contains("dbo.Orders.CustomerId", output);
        Assert.Contains("Recommendations:", output);
        Assert.Contains("ForeignKey", output);
        Assert.Contains("Informational insights:", output);
        Assert.Contains("Sampling truncated", output);
    }

    [Fact]
    public void EmitSchemaApplySummary_IncludesStaticSeedValidationStatus()
    {
        var console = new TestConsole();

        var applyResult = new SchemaApplyResult(
            Attempted: true,
            SafeScriptApplied: true,
            StaticSeedsApplied: false,
            AppliedScripts: ImmutableArray.Create("safe.sql"),
            AppliedSeedScripts: ImmutableArray<string>.Empty,
            SkippedScripts: ImmutableArray.Create("seed.sql"),
            Warnings: ImmutableArray<string>.Empty,
            PendingRemediationCount: 0,
            SafeScriptPath: "safe.sql",
            RemediationScriptPath: "remediation.sql",
            StaticSeedScriptPaths: ImmutableArray.Create("seed.sql"),
            Duration: TimeSpan.FromSeconds(5),
            StaticSeedSynchronizationMode: StaticSeedSynchronizationMode.ValidateThenApply,
            StaticSeedValidation: StaticSeedValidationSummary.Failure("Static entity seed data drift detected."));

        CommandConsole.EmitSchemaApplySummary(console, applyResult);

        var standardOutput = console.Out!.ToString() ?? string.Empty;
        var errorOutput = console.Error!.ToString() ?? string.Empty;

        Assert.Contains("Static seed mode: ValidateThenApply (validation failed)", standardOutput);
        Assert.Contains("[validation] Static entity seed data drift detected.", errorOutput);
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
    public void EmitSqlProfilerSnapshot_SummarizesKeyAnomalies()
    {
        var console = new TestConsole();

        var notNullColumn = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Status"),
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 1_000,
            nullCount: 125,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_000)).Value;

        var coverageColumn = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("LastLogin"),
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 1_000,
            nullCount: 0,
            ProfilingProbeStatus.CreateFallbackTimeout(DateTimeOffset.UnixEpoch, 250)).Value;

        var uniqueCandidate = UniqueCandidateProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Email"),
            hasDuplicate: true,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 0)).Value;

        var fkReference = ForeignKeyReference.Create(
            new SchemaName("dbo"),
            new TableName("Orders"),
            new ColumnName("CustomerId"),
            new SchemaName("dbo"),
            new TableName("Customers"),
            new ColumnName("Id"),
            hasDatabaseConstraint: true).Value;

        var fkSample = ForeignKeyOrphanSample.Create(
            ImmutableArray.Create("Id"),
            "CustomerId",
            ImmutableArray.Create(new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(42), 99)),
            totalOrphans: 2);

        var fkReality = ForeignKeyReality.Create(
            fkReference,
            hasOrphan: true,
            orphanCount: 2,
            isNoCheck: true,
            ProfilingProbeStatus.CreateFallbackTimeout(DateTimeOffset.UnixEpoch, 500),
            fkSample).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { notNullColumn, coverageColumn },
            new[] { uniqueCandidate },
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { fkReality }).Value;

        CommandConsole.EmitSqlProfilerSnapshot(console, snapshot);

        var output = console.Out!.ToString() ?? string.Empty;

        Assert.Contains("SQL profiler snapshot:", output);
        Assert.Contains("Nulls in NOT NULL columns:", output);
        Assert.Contains("dbo.Users.Status", output);
        Assert.Contains("NULL counting coverage issues:", output);
        Assert.Contains("dbo.Users.LastLogin", output);
        Assert.Contains("Unique constraint risks:", output);
        Assert.Contains("dbo.Users.Email", output);
        Assert.Contains("Summary: 1 critical.", output);
        Assert.Contains("Foreign key anomalies:", output);
        Assert.Contains("dbo.Orders.CustomerId -> dbo.Customers.Id", output);
        Assert.Contains("Summary: 1 critical.", output);
        Assert.Contains("Sample rows violating NOT NULL:", output);
        Assert.Contains("Foreign key orphan samples:", output);
        Assert.DoesNotContain("\"columns\"", output);
    }

    [Fact]
    public void EmitMultiEnvironmentReport_WritesTableAndFindings()
    {
        var console = new TestConsole();

        var statusPrimary = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Status"),
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 100,
            nullCount: 0,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_000)).Value;

        var statusSecondary = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Status"),
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 150,
            nullCount: 0,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_500)).Value;

        var emailPrimary = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Email"),
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: true,
            defaultDefinition: null,
            rowCount: 100,
            nullCount: 0,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_000)).Value;

        var emailSecondary = ColumnProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Email"),
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: true,
            defaultDefinition: null,
            rowCount: 150,
            nullCount: 5,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_500)).Value;

        var idUniquePrimary = UniqueCandidateProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Id"),
            hasDuplicate: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_000)).Value;

        var idUniqueSecondary = UniqueCandidateProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Id"),
            hasDuplicate: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_500)).Value;

        var emailUniquePrimary = UniqueCandidateProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Email"),
            hasDuplicate: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_000)).Value;

        var emailUniqueSecondary = UniqueCandidateProfile.Create(
            new SchemaName("dbo"),
            new TableName("Users"),
            new ColumnName("Email"),
            hasDuplicate: true,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_500)).Value;

        var foreignKeyReference = ForeignKeyReference.Create(
            new SchemaName("dbo"),
            new TableName("Orders"),
            new ColumnName("CustomerId"),
            new SchemaName("dbo"),
            new TableName("Customers"),
            new ColumnName("Id"),
            hasDatabaseConstraint: true).Value;

        var foreignKeyPrimary = ForeignKeyReality.Create(
            foreignKeyReference,
            hasOrphan: false,
            orphanCount: 0,
            isNoCheck: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_000),
            orphanSample: null).Value;

        var secondarySample = ForeignKeyOrphanSample.Create(
            ImmutableArray.Create("Id"),
            "CustomerId",
            ImmutableArray.Create(new ForeignKeyOrphanIdentifier(ImmutableArray.Create<object?>(77), 101)),
            totalOrphans: 3);

        var foreignKeySecondary = ForeignKeyReality.Create(
            foreignKeyReference,
            hasOrphan: true,
            orphanCount: 3,
            isNoCheck: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 1_500),
            secondarySample).Value;

        var primarySnapshot = ProfileSnapshot.Create(
            new[] { statusPrimary, emailPrimary },
            new[] { idUniquePrimary, emailUniquePrimary },
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { foreignKeyPrimary }).Value;

        var secondarySnapshot = ProfileSnapshot.Create(
            new[] { statusSecondary, emailSecondary },
            new[] { idUniqueSecondary, emailUniqueSecondary },
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { foreignKeySecondary }).Value;

        var report = MultiEnvironmentProfileReport.Create(
            new[]
            {
                new ProfilingEnvironmentSnapshot(
                    "Primary (Sample)",
                    true,
                    MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                    false,
                    primarySnapshot,
                    TimeSpan.FromSeconds(12)),
                new ProfilingEnvironmentSnapshot(
                    "QA",
                    false,
                    MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided,
                    false,
                    secondarySnapshot,
                    TimeSpan.FromSeconds(45)),
            });

        CommandConsole.EmitMultiEnvironmentReport(console, report);

        var output = console.Out!.ToString()!;
        var normalized = Regex.Replace(output, "\\s+", " ").Trim();

        Assert.Contains("Multi-environment profiling summary:", normalized);
        Assert.Contains("Environment | Role | Label Source", normalized);
        Assert.Contains("⭐ Primary (Sample) | Primary | Provided", normalized);
        Assert.Contains("QA | Secondary | Provided", normalized);
        Assert.Contains("Environment findings:", normalized);
        Assert.Contains("QA: NOT NULL violations", normalized);
        Assert.Contains("Remediate null values or adjust policy exclusions before enforcing NOT NULL constraints.", normalized);
        Assert.Contains("QA: orphaned foreign keys", normalized);
        Assert.Contains("Repair orphaned relationships or adjust policy exclusions before enforcing foreign keys.", normalized);
        Assert.Contains("Review QA data quality", normalized);
        Assert.Contains("Constraint consensus across environments:", normalized);
        Assert.Contains("Analyzed 2 environments: 2/5 constraints safe to apply (40.0 % consensus)", normalized);
        Assert.Contains("NOT NULL: 1 safe / 1 risky | UNIQUE: 1 safe / 1 risky | FOREIGN KEY: 0 safe / 1 risky", normalized);
        Assert.Contains("DDL readiness blockers:", normalized);
        Assert.Contains("NOT NULL: 1 blocked.", normalized);
        Assert.Contains("UNIQUE: 1 blocked.", normalized);
        Assert.Contains("FOREIGN KEY: 1 blocked.", normalized);
        Assert.Contains("Constraints safe across all environments:", normalized);
        Assert.Contains("Constraints requiring remediation before DDL enforcement:", normalized);
        Assert.Contains("NOT NULL", output);
        Assert.Contains("dbo.Users.Status", output);
        Assert.Contains("dbo.Users.Email", output);
        Assert.Contains("FOREIGN KEY", output);
        Assert.Contains("dbo.Orders.CustomerId -> dbo.Customers.Id", output);
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

    [Fact]
    public void EmitBuildSsdtSummary_WritesCompactBlock()
    {
        var console = new TestConsole();

        var snapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var columns = ImmutableArray.Create(
            new ColumnDecisionReport(
                new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("CustomerId")),
                true,
                false,
                ImmutableArray<string>.Empty),
            new ColumnDecisionReport(
                new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("Notes")),
                false,
                true,
                ImmutableArray<string>.Empty));

        var uniqueIndexes = ImmutableArray.Create(
            new UniqueIndexDecisionReport(
                new IndexCoordinate(new SchemaName("dbo"), new TableName("Orders"), new IndexName("IX_Orders_Customer")),
                true,
                false,
                ImmutableArray<string>.Empty));

        var foreignKeys = ImmutableArray.Create(
            new ForeignKeyDecisionReport(
                new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("CustomerId")),
                true,
                false,
                ImmutableArray<string>.Empty));

        var decisionReport = new PolicyDecisionReport(
            columns,
            uniqueIndexes,
            foreignKeys,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            ImmutableDictionary<string, ToggleExportValue>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            TighteningToggleSnapshot.Create(TighteningOptions.Default));

        var manifest = new SsdtManifest(
            new[]
            {
                new TableManifestEntry(
                    "Sales",
                    "dbo",
                    "Orders",
                    "dbo.Orders.sql",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    true),
            },
            new SsdtManifestOptions(false, false, false, 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(0, 0, 0),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());

        var opportunities = new Osm.Validation.Tightening.Opportunities.OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty
                .Add(OpportunityCategory.Contradiction, 2)
                .Add(OpportunityCategory.Recommendation, 3),
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UnixEpoch);

        var validations = ValidationReport.Empty(DateTimeOffset.UnixEpoch);

        var pipelineResult = new BuildSsdtPipelineResult(
            snapshot,
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            "decision-log.json",
            "opportunities.json",
            "validations.json",
            "safe.sql",
            string.Empty,
            "remediation.sql",
            string.Empty,
            Path.Combine("/tmp/output", "OutSystemsModel.sqlproj"),
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            DynamicInsertOutputMode: DynamicInsertOutputMode.PerEntity,
            ImmutableArray<string>.Empty,
            MultiEnvironmentProfileReport.Empty);

        var applicationResult = new BuildSsdtApplicationResult(
            pipelineResult,
            "fixture",
            "profile.json",
            "/tmp/output",
            "model.json",
            false,
            ImmutableArray<string>.Empty,
            DynamicEntityExtractionTelemetry.Empty);

        CommandConsole.EmitBuildSsdtSummary(console, applicationResult, pipelineResult);

        var output = console.Out!.ToString() ?? string.Empty;

        Assert.Contains("SSDT build summary:", output);
        Assert.Contains("Output: /tmp/output", output);
        Assert.Contains("Manifest: /tmp/output/manifest.json", output);
        Assert.Contains("Decision log: decision-log.json", output);
        Assert.Contains("Opportunities: opportunities.json", output);
        Assert.Contains("Validations: validations.json", output);
        Assert.Contains("Safe script: safe.sql (3 ready)", output);
        Assert.Contains("Remediation script: remediation.sql (⚠️ 2 contradictions)", output);
        Assert.Contains("Dynamic insert mode: PerEntity", output);
        Assert.Contains("Tightening: Columns 1/2, Unique 1/1, Foreign Keys 1/1", output);
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
