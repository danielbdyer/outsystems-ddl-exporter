using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Cli;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Application;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Cli.Commands;

internal static class CommandConsole
{
    private static readonly JsonSerializerOptions NamingOverrideSerializerOptions = new()
    {
        WriteIndented = true
    };

    private const int DefaultTableLimit = 20;

    private enum IssueSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    private sealed record UniqueIssue(IssueSeverity Severity, string Scope, string Target, string Details, string Probe);

    private sealed record ForeignKeyIssue(IssueSeverity Severity, string Reference, string Details, string Probe);

    public static async Task EmitBuildSsdtRunAsync(
        IConsole console,
        BuildSsdtApplicationResult applicationResult,
        BuildSsdtPipelineResult pipelineResult,
        bool openReport,
        CancellationToken cancellationToken)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (applicationResult is null)
        {
            throw new ArgumentNullException(nameof(applicationResult));
        }

        if (pipelineResult is null)
        {
            throw new ArgumentNullException(nameof(pipelineResult));
        }

        if (!string.IsNullOrWhiteSpace(applicationResult.ModelPath))
        {
            var modelMessage = applicationResult.ModelWasExtracted
                ? $"Extracted model to {applicationResult.ModelPath}."
                : $"Using model at {applicationResult.ModelPath}.";
            WriteLine(console, modelMessage);
        }

        if (!applicationResult.ModelExtractionWarnings.IsDefaultOrEmpty && applicationResult.ModelExtractionWarnings.Length > 0)
        {
            EmitPipelineWarnings(console, applicationResult.ModelExtractionWarnings);
        }

        var profilerProvider = applicationResult.ProfilerProvider;
        if (IsSqlProfiler(profilerProvider))
        {
            EmitSqlProfilerSnapshot(console, pipelineResult.Profile);
            EmitMultiEnvironmentReport(console, pipelineResult.MultiEnvironmentReport);
        }

        EmitBuildSsdtSummary(console, applicationResult, pipelineResult);
        EmitContradictionDetails(console, pipelineResult.Opportunities);

        EmitTighteningDiagnostics(console, pipelineResult.DecisionReport.Diagnostics);

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            WriteLine(console, $"Cached inputs to {cacheResult.CacheDirectory} (key {cacheResult.Manifest.Key}).");
        }

        EmitSqlValidationSummary(console, pipelineResult);

        var pipelineWarnings = pipelineResult.Warnings
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .ToImmutableArray();

        var actionableProfilingInsights = pipelineResult.ProfilingInsights
            .Where(static insight => insight is
            {
                Severity: ProfilingInsightSeverity.Warning or ProfilingInsightSeverity.Error
                    or ProfilingInsightSeverity.Recommendation,
            })
            .ToImmutableArray();

        var hasPipelineLogEntries = pipelineResult.ExecutionLog.Entries.Count > 0;

        if ((pipelineWarnings.Length > 0 || actionableProfilingInsights.Length > 0) && hasPipelineLogEntries)
        {
            EmitPipelineLog(console, pipelineResult.ExecutionLog);
        }

        if (pipelineWarnings.Length > 0)
        {
            EmitPipelineWarnings(console, pipelineWarnings);
        }

        if (actionableProfilingInsights.Length > 0)
        {
            EmitProfilingInsights(console, actionableProfilingInsights);
        }

        EmitSsdtEmissionSummary(console, applicationResult, pipelineResult);

        EmitModuleRollups(
            console,
            pipelineResult.ModuleManifestRollups,
            pipelineResult.DecisionReport.ModuleRollups);

        EmitTogglePrecedence(console, pipelineResult.DecisionReport.TogglePrecedence);

        foreach (var summary in PolicyDecisionSummaryFormatter.FormatForConsole(pipelineResult.DecisionReport))
        {
            WriteLine(console, summary);
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "Tightening Artifacts:");
        WriteLine(console, $"  Decision log: {pipelineResult.DecisionLogPath}");
        WriteLine(console, $"  Opportunities report: {pipelineResult.OpportunitiesPath}");
        WriteLine(console, $"  Validations report: {pipelineResult.ValidationsPath}");

        if (pipelineResult.Opportunities.ContradictionCount > 0)
        {
            WriteLine(console, $"  ⚠️  Needs remediation ({pipelineResult.Opportunities.ContradictionCount} contradictions): {pipelineResult.RemediationScriptPath}");
        }
        else
        {
            WriteLine(console, $"  Needs remediation: {pipelineResult.RemediationScriptPath}");
        }

        WriteLine(console, $"  Safe to apply ({pipelineResult.Opportunities.RecommendationCount} opportunities): {pipelineResult.SafeScriptPath}");

        if (!string.IsNullOrWhiteSpace(applicationResult.OutputDirectory))
        {
            var manifestPath = Path.Combine(applicationResult.OutputDirectory, FullExportVerb.RunManifestFileName);
            if (File.Exists(manifestPath))
            {
                WriteLine(console, $"  Run manifest: {manifestPath}");
            }
        }

        if (!openReport)
        {
            return;
        }

        try
        {
            var reportPath = await PipelineReportLauncher
                .GenerateAsync(applicationResult, cancellationToken)
                .ConfigureAwait(false);
            WriteLine(console, $"Report written to {reportPath}");
            PipelineReportLauncher.TryOpen(reportPath, console);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteErrorLine(console, $"[warning] Failed to open report: {ex.Message}");
        }
    }

    public static void EmitBuildSsdtSummary(
        IConsole console,
        BuildSsdtApplicationResult applicationResult,
        BuildSsdtPipelineResult pipelineResult)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (applicationResult is null)
        {
            throw new ArgumentNullException(nameof(applicationResult));
        }

        if (pipelineResult is null)
        {
            throw new ArgumentNullException(nameof(pipelineResult));
        }

        var outputDirectory = string.IsNullOrWhiteSpace(applicationResult.OutputDirectory)
            ? string.Empty
            : applicationResult.OutputDirectory;
        var manifestPath = string.IsNullOrWhiteSpace(outputDirectory)
            ? "manifest.json"
            : Path.Combine(outputDirectory, "manifest.json");
        var decisionReport = pipelineResult.DecisionReport;
        var opportunities = pipelineResult.Opportunities;
        var validations = pipelineResult.Validations;
        var contradictionCount = opportunities?.ContradictionCount ?? 0;
        var readyOpportunities = opportunities?.RecommendationCount ?? 0;
        var validationCount = validations?.TotalCount ?? 0;

        WriteLine(console, string.Empty);
        WriteLine(console, "SSDT build summary:");
        WriteLine(console, $"  Output: {FormatPath(outputDirectory)}");
        WriteLine(console, $"  Manifest: {FormatPath(manifestPath)}");
        WriteLine(console, $"  Decision log: {FormatPath(pipelineResult.DecisionLogPath)}");
        WriteLine(console, $"  Opportunities: {FormatPath(pipelineResult.OpportunitiesPath)}");
        WriteLine(console, FormatValidationsLine(pipelineResult.ValidationsPath, validationCount));
        WriteLine(console, FormatSafeScriptLine(pipelineResult.SafeScriptPath, readyOpportunities));
        WriteLine(console, FormatRemediationScriptLine(pipelineResult.RemediationScriptPath, contradictionCount));

        if (decisionReport is not null)
        {
            WriteLine(
                console,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  Tightening: Columns {0}/{1}, Unique {2}/{3}, Foreign Keys {4}/{5}",
                    decisionReport.TightenedColumnCount,
                    decisionReport.ColumnCount,
                    decisionReport.UniqueIndexesEnforcedCount,
                    decisionReport.UniqueIndexCount,
                    decisionReport.ForeignKeysCreatedCount,
                    decisionReport.ForeignKeyCount));
        }
    }

    public static void EmitProfileSummary(
        IConsole console,
        CaptureProfileApplicationResult applicationResult)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (applicationResult is null)
        {
            throw new ArgumentNullException(nameof(applicationResult));
        }

        var pipelineResult = applicationResult.PipelineResult
            ?? throw new ArgumentNullException(nameof(applicationResult.PipelineResult));

        if (!string.IsNullOrWhiteSpace(applicationResult.ModelPath))
        {
            WriteLine(console, $"Using model at {applicationResult.ModelPath}.");
        }

        EmitPipelineLog(console, pipelineResult.ExecutionLog);
        EmitPipelineWarnings(console, pipelineResult.Warnings);
        EmitProfilingInsights(console, pipelineResult.Insights);

        if (IsSqlProfiler(applicationResult.ProfilerProvider))
        {
            EmitSqlProfilerSnapshot(console, pipelineResult.Profile);
            EmitMultiEnvironmentReport(console, pipelineResult.MultiEnvironmentReport);
        }

        WriteLine(console, $"Profile written to {pipelineResult.ProfilePath}");
        WriteLine(console, $"Manifest written to {pipelineResult.ManifestPath}");
    }

    public static void EmitSchemaApplySummary(IConsole console, SchemaApplyResult applyResult)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (applyResult is null)
        {
            throw new ArgumentNullException(nameof(applyResult));
        }

        if (!applyResult.Attempted)
        {
            WriteLine(console, "  Schema apply skipped.");
        }
        else
        {
            WriteLine(console, $"  Safe script applied: {(applyResult.SafeScriptApplied ? "yes" : "no")} ({applyResult.SafeScriptPath ?? "<none>"})");

            var appliedSeedCount = applyResult.AppliedSeedScripts.IsDefaultOrEmpty ? 0 : applyResult.AppliedSeedScripts.Length;
            var totalSeedCount = applyResult.StaticSeedScriptPaths.IsDefaultOrEmpty ? 0 : applyResult.StaticSeedScriptPaths.Length;
            WriteLine(console, $"  Static seeds applied: {(applyResult.StaticSeedsApplied ? "yes" : "no")} ({appliedSeedCount}/{totalSeedCount})");
            WriteLine(console, $"  Duration: {applyResult.Duration.TotalSeconds:F2}s");
        }

        WriteLine(console, $"  Pending remediation count: {applyResult.PendingRemediationCount}");
        WriteLine(console, $"  Remediation script: {applyResult.RemediationScriptPath ?? "<none>"}");

        if (!applyResult.SkippedScripts.IsDefaultOrEmpty && applyResult.SkippedScripts.Length > 0)
        {
            WriteLine(console, "  Skipped scripts:");
            foreach (var skipped in applyResult.SkippedScripts)
            {
                WriteLine(console, $"    - {skipped}");
            }
        }

        if (!applyResult.Warnings.IsDefaultOrEmpty && applyResult.Warnings.Length > 0)
        {
            WriteLine(console, "  Warnings:");
            foreach (var warning in applyResult.Warnings)
            {
                WriteErrorLine(console, $"    [warning] {warning}");
            }
        }
    }

    public static void EmitExtractModelSummary(
        IConsole console,
        ExtractModelApplicationResult applicationResult,
        string resolvedOutputPath)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (applicationResult is null)
        {
            throw new ArgumentNullException(nameof(applicationResult));
        }

        var extractionResult = applicationResult.ExtractionResult
            ?? throw new ArgumentNullException(nameof(applicationResult.ExtractionResult));

        var model = extractionResult.Model;
        var moduleCount = model.Modules.Length;
        var entityCount = model.Modules.Sum(static m => m.Entities.Length);
        var attributeCount = model.Modules.Sum(static m => m.Entities.Sum(static e => e.Attributes.Length));

        if (extractionResult.Warnings.Count > 0)
        {
            foreach (var warning in extractionResult.Warnings)
            {
                WriteErrorLine(console, $"Warning: {warning}");
            }
        }

        WriteLine(console, $"Extracted {moduleCount} modules spanning {entityCount} entities.");
        WriteLine(console, $"Attributes: {attributeCount}");
        WriteLine(console, $"Model written to {resolvedOutputPath}.");
        WriteLine(console, $"Extraction timestamp (UTC): {extractionResult.ExtractedAtUtc:O}");
    }

    public static void EmitTighteningDiagnostics(IConsole console, ImmutableArray<TighteningDiagnostic> diagnostics)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (diagnostics.IsDefaultOrEmpty || diagnostics.Length == 0)
        {
            return;
        }

        var warnings = diagnostics.Where(static d => d.Severity == TighteningDiagnosticSeverity.Warning).ToArray();

        if (warnings.Length > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, $"Tightening diagnostics: {warnings.Length} warning(s)");
            foreach (var diagnostic in warnings)
            {
                WriteErrorLine(console, $"  [warning] {diagnostic.Message}");
            }
        }

        EmitNamingOverrideTemplate(console, diagnostics);
    }

    public static void EmitSsdtEmissionSummary(
        IConsole console,
        BuildSsdtApplicationResult applicationResult,
        BuildSsdtPipelineResult pipelineResult)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (applicationResult is null)
        {
            throw new ArgumentNullException(nameof(applicationResult));
        }

        if (pipelineResult is null)
        {
            throw new ArgumentNullException(nameof(pipelineResult));
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "SSDT Emission Summary:");
        WriteLine(console, $"  Tables: {pipelineResult.Manifest.Tables.Count} emitted to {applicationResult.OutputDirectory}");
        WriteLine(console, $"  Manifest: {Path.Combine(applicationResult.OutputDirectory, "manifest.json")}");

        var seedPaths = pipelineResult.StaticSeedScriptPaths;
        var seedCount = seedPaths.IsDefaultOrEmpty ? 0 : seedPaths.Length;
        var seedRoot = FullExportRunManifest.ResolveStaticSeedRoot(pipelineResult);

        if (seedCount > 0)
        {
            WriteLine(console, $"  Seed artifacts: {seedCount} file(s)");
            foreach (var seedPath in seedPaths.Take(3))
            {
                WriteLine(console, $"    - {seedPath}");
            }

            if (seedCount > 3)
            {
                WriteLine(console, $"    ... {seedCount - 3} more");
            }
        }
        else
        {
            WriteLine(console, "  Seed artifacts: none");
        }

        var dynamicPaths = pipelineResult.DynamicInsertScriptPaths;
        var dynamicCount = dynamicPaths.IsDefaultOrEmpty ? 0 : dynamicPaths.Length;
        if (dynamicCount > 0)
        {
            WriteLine(console, $"  Dynamic INSERT scripts: {dynamicCount} file(s)");
            foreach (var dynamicPath in dynamicPaths.Take(3))
            {
                WriteLine(console, $"    - {dynamicPath}");
            }

            if (dynamicCount > 3)
            {
                WriteLine(console, $"    ... {dynamicCount - 3} more");
            }
        }
        else
        {
            WriteLine(console, "  Dynamic INSERT scripts: none");
        }

        if (!pipelineResult.TelemetryPackagePaths.IsDefaultOrEmpty && pipelineResult.TelemetryPackagePaths.Length > 0)
        {
            WriteLine(console, $"  Telemetry packages: {pipelineResult.TelemetryPackagePaths.Length} file(s)");
            foreach (var packagePath in pipelineResult.TelemetryPackagePaths.Take(3))
            {
                WriteLine(console, $"    - {packagePath}");
            }

            if (pipelineResult.TelemetryPackagePaths.Length > 3)
            {
                WriteLine(console, $"    ... {pipelineResult.TelemetryPackagePaths.Length - 3} more");
            }
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "Manifest semantics:");
        WriteLine(console, $"  Dynamic artifact root: {applicationResult.OutputDirectory}");
        WriteLine(console, $"  Static seed root: {seedRoot ?? "<none>"}");
        WriteLine(console, $"  Seeds mirrored in dynamic manifest: {(FullExportRunManifest.DefaultIncludeStaticSeedArtifactsInDynamic ? "yes" : "no")}");

        WriteLine(console, string.Empty);
        WriteLine(console, "Tightening Statistics:");
        WriteLine(console, $"  Columns: {pipelineResult.DecisionReport.TightenedColumnCount}/{pipelineResult.DecisionReport.ColumnCount} confirmed NOT NULL");
        WriteLine(console, $"  Unique indexes: {pipelineResult.DecisionReport.UniqueIndexesEnforcedCount}/{pipelineResult.DecisionReport.UniqueIndexCount} confirmed UNIQUE");
        WriteLine(console, $"  Foreign keys: {pipelineResult.DecisionReport.ForeignKeysCreatedCount}/{pipelineResult.DecisionReport.ForeignKeyCount} safe to create");

        EmitTighteningStatisticsDetails(console, pipelineResult.DecisionReport);
    }

    public static void EmitSqlValidationSummary(IConsole console, BuildSsdtPipelineResult pipelineResult)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (pipelineResult is null)
        {
            throw new ArgumentNullException(nameof(pipelineResult));
        }

        var summary = pipelineResult.SqlValidation ?? SsdtSqlValidationSummary.Empty;

        WriteLine(console, string.Empty);
        WriteLine(console, "SQL Validation:");
        WriteLine(console, $"  Files: {summary.TotalFiles} validated, {summary.FilesWithErrors} with errors");
        WriteLine(console, $"  Errors: {summary.ErrorCount} total");

        if (summary.ErrorCount <= 0 || summary.Issues.IsDefaultOrEmpty || summary.Issues.Length == 0)
        {
            return;
        }

        const int MaxSamples = 5;
        WriteLine(console, string.Empty);
        WriteErrorLine(console, "  Error samples:");

        var samples = summary.Issues
            .Where(static issue => issue is not null)
            .SelectMany(issue => issue.Errors.Select(error => (issue.Path, error)))
            .Take(MaxSamples)
            .ToArray();

        foreach (var sample in samples)
        {
            var error = sample.error;
            WriteErrorLine(
                console,
                $"    {sample.Path}:{error.Line}:{error.Column} [#{error.Number}] {error.Message}");
        }

        var remaining = summary.ErrorCount - samples.Length;
        if (remaining > 0)
        {
            WriteErrorLine(console, $"    ... {remaining} additional error(s) omitted");
        }
    }

    private static bool IsSqlProfiler(string provider)
        => string.Equals(provider, "sql", StringComparison.OrdinalIgnoreCase);

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
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        WriteLine(console, "SQL profiler snapshot:");

        var totalColumns = snapshot.Columns.Length;
        var totalUniqueCandidates = snapshot.UniqueCandidates.Length + snapshot.CompositeUniqueCandidates.Length;
        var totalForeignKeys = snapshot.ForeignKeys.Length;

        var columnsWithNulls = snapshot.Columns.Count(static column => column.NullCount > 0);
        var notNullViolations = snapshot.Columns.Count(static column => !column.IsNullablePhysical && column.NullCount > 0);
        var columnProbeIssues = snapshot.Columns
            .Where(static column => column.NullCountStatus.Outcome != ProfilingProbeOutcome.Succeeded)
            .ToList();

        var uniqueIssues = BuildUniqueIssues(snapshot);
        var foreignKeyIssues = BuildForeignKeyIssues(snapshot);

        WriteLine(
            console,
            $"  Columns: {totalColumns:N0} profiled, {columnsWithNulls:N0} with NULLs, {notNullViolations:N0} violating NOT NULL");
        WriteLine(
            console,
            $"  Unique checks: {totalUniqueCandidates:N0} candidates, {uniqueIssues.Count:N0} issue(s)");
        WriteLine(
            console,
            $"  Foreign keys: {totalForeignKeys:N0} relationships, {foreignKeyIssues.Count:N0} anomaly/anomalies");

        if (columnProbeIssues.Count > 0)
        {
            WriteLine(console, $"  NULL probe coverage warnings: {columnProbeIssues.Count:N0}");
        }

        if (notNullViolations > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "🔴 Nulls in NOT NULL columns:");

            var rows = snapshot.Columns
                .Where(static column => !column.IsNullablePhysical && column.NullCount > 0)
                .OrderByDescending(static column => column.NullCount)
                .ThenBy(static column => column.Schema.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static column => column.Table.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static column => column.Column.Value, StringComparer.OrdinalIgnoreCase)
                .Take(DefaultTableLimit)
                .Select(column => (IReadOnlyList<string>)ImmutableArray.Create(
                    FormatColumnCoordinate(column),
                    column.NullCount.ToString("N0", CultureInfo.InvariantCulture),
                    FormatPercentage(column.NullCount, column.RowCount),
                    column.RowCount.ToString("N0", CultureInfo.InvariantCulture),
                    GetSeverityLabel(column.RowCount, column.NullCount),
                    FormatColumnFlags(column),
                    FormatProbeStatus(column.NullCountStatus)))
                .ToImmutableArray();

            var headers = ImmutableArray.Create(
                "Column",
                "Nulls",
                "Null %",
                "Rows",
                "Severity",
                "Flags",
                "Probe");

            WriteTable(console, headers, rows);
            EmitTableOverflow(console, notNullViolations, rows.Length);
            EmitNullViolationSamples(console, snapshot);
        }

        if (columnProbeIssues.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "⚠️  NULL counting coverage issues:");

            var rows = columnProbeIssues
                .OrderByDescending(static column => column.NullCountStatus.SampleSize)
                .ThenBy(static column => column.Schema.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static column => column.Table.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static column => column.Column.Value, StringComparer.OrdinalIgnoreCase)
                .Take(DefaultTableLimit)
                .Select(column => (IReadOnlyList<string>)ImmutableArray.Create(
                    FormatColumnCoordinate(column),
                    column.RowCount.ToString("N0", CultureInfo.InvariantCulture),
                    FormatProbeStatus(column.NullCountStatus),
                    GetProbeIssueDetail(column.NullCountStatus)))
                .ToImmutableArray();

            var headers = ImmutableArray.Create(
                "Column",
                "Rows",
                "Probe",
                "Details");

            WriteTable(console, headers, rows);
            EmitTableOverflow(console, columnProbeIssues.Count, rows.Length);
        }

        if (uniqueIssues.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "⚠️  Unique constraint risks:");

            WriteLine(console, $"  Summary: {FormatIssueSeverityCounts(uniqueIssues.Select(static issue => issue.Severity))}.");
            var rows = uniqueIssues
                .OrderByDescending(static issue => issue.Severity)
                .ThenBy(static issue => issue.Scope, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static issue => issue.Target, StringComparer.OrdinalIgnoreCase)
                .Take(DefaultTableLimit)
                .Select(issue => (IReadOnlyList<string>)ImmutableArray.Create(
                    FormatIssueSeverity(issue.Severity),
                    issue.Scope,
                    issue.Target,
                    issue.Details,
                    issue.Probe))
                .ToImmutableArray();

            var headers = ImmutableArray.Create(
                "Severity",
                "Scope",
                "Target",
                "Details",
                "Probe");

            WriteTable(console, headers, rows);
            EmitTableOverflow(console, uniqueIssues.Count, rows.Length);
        }

        if (foreignKeyIssues.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "⚠️  Foreign key anomalies:");

            WriteLine(console, $"  Summary: {FormatIssueSeverityCounts(foreignKeyIssues.Select(static issue => issue.Severity))}.");
            var rows = foreignKeyIssues
                .OrderByDescending(static issue => issue.Severity)
                .ThenBy(static issue => issue.Reference, StringComparer.OrdinalIgnoreCase)
                .Take(DefaultTableLimit)
                .Select(issue => (IReadOnlyList<string>)ImmutableArray.Create(
                    FormatIssueSeverity(issue.Severity),
                    issue.Reference,
                    issue.Details,
                    issue.Probe))
                .ToImmutableArray();

            var headers = ImmutableArray.Create(
                "Severity",
                "Reference",
                "Details",
                "Probe");

            WriteTable(console, headers, rows);
            EmitTableOverflow(console, foreignKeyIssues.Count, rows.Length);
            EmitForeignKeyOrphanSamples(console, snapshot);
        }

        if (notNullViolations == 0
            && columnProbeIssues.Count == 0
            && uniqueIssues.Count == 0
            && foreignKeyIssues.Count == 0)
        {
            WriteLine(console, "  No anomalous findings detected in profiler snapshot.");
        }
    }

    private static IReadOnlyList<UniqueIssue> BuildUniqueIssues(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var issues = new List<UniqueIssue>();

        foreach (var candidate in snapshot.UniqueCandidates)
        {
            if (candidate is null)
            {
                continue;
            }

            var severity = IssueSeverity.Info;
            var details = new List<string>();

            if (candidate.HasDuplicate)
            {
                severity = IssueSeverity.Critical;
                details.Add("Duplicate values detected");
            }

            if (candidate.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded)
            {
                var probeSeverity = MapProbeSeverity(candidate.ProbeStatus);
                severity = MaxSeverity(severity, probeSeverity);
                details.Add(GetProbeIssueDetail(candidate.ProbeStatus));
            }

            if (details.Count == 0)
            {
                continue;
            }

            issues.Add(new UniqueIssue(
                severity,
                "Single",
                FormatUniqueTarget(candidate.Schema.Value, candidate.Table.Value, candidate.Column.Value),
                string.Join("; ", details),
                FormatProbeStatus(candidate.ProbeStatus)));
        }

        foreach (var composite in snapshot.CompositeUniqueCandidates)
        {
            if (composite is null || !composite.HasDuplicate)
            {
                continue;
            }

            var columns = string.Join(", ", composite.Columns.Select(static column => column.Value));
            issues.Add(new UniqueIssue(
                IssueSeverity.Critical,
                "Composite",
                $"{composite.Schema.Value}.{composite.Table.Value} ({columns})",
                "Duplicate composite key values detected",
                "--"));
        }

        return issues;
    }

    private static IReadOnlyList<ForeignKeyIssue> BuildForeignKeyIssues(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var issues = new List<ForeignKeyIssue>();

        foreach (var fk in snapshot.ForeignKeys)
        {
            if (fk is null)
            {
                continue;
            }

            var details = new List<(IssueSeverity Severity, string Detail)>();
            var hasConstraint = fk.Reference.HasDatabaseConstraint;
            var orphanCount = Math.Max(fk.OrphanCount, 0);

            if (fk.HasOrphan)
            {
                var enforcementContext = hasConstraint
                    ? fk.IsNoCheck
                        ? "constraint exists but is marked WITH NOCHECK (untrusted)"
                        : "constraint exists and should block these rows"
                    : "no database constraint currently prevents orphan inserts";
                var orphanDetail = string.Format(
                    CultureInfo.InvariantCulture,
                    "Orphaned rows detected ({0:N0}) – {1}",
                    orphanCount,
                    enforcementContext);
                details.Add((IssueSeverity.Critical, orphanDetail));
            }

            if (fk.IsNoCheck && !fk.HasOrphan)
            {
                details.Add((IssueSeverity.Warning, "Constraint defined as NO CHECK"));
            }

            if (fk.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded)
            {
                var probeSeverity = MapProbeSeverity(fk.ProbeStatus);
                details.Add((probeSeverity, GetProbeIssueDetail(fk.ProbeStatus)));
            }

            if (details.Count == 0)
            {
                continue;
            }

            var severity = details.Select(static tuple => tuple.Severity).Aggregate(IssueSeverity.Info, MaxSeverity);
            var detailText = string.Join("; ", details.Select(static tuple => tuple.Detail));
            issues.Add(new ForeignKeyIssue(
                severity,
                FormatForeignKeyReference(fk.Reference),
                detailText,
                FormatProbeStatus(fk.ProbeStatus)));
        }

        return issues;
    }

    private static IssueSeverity MapProbeSeverity(ProfilingProbeStatus status)
        => status.Outcome switch
        {
            ProfilingProbeOutcome.FallbackTimeout => IssueSeverity.Warning,
            ProfilingProbeOutcome.Cancelled => IssueSeverity.Warning,
            ProfilingProbeOutcome.Unknown => IssueSeverity.Info,
            _ => IssueSeverity.Info
        };

    private static IssueSeverity MaxSeverity(IssueSeverity current, IssueSeverity candidate)
        => (IssueSeverity)Math.Max((int)current, (int)candidate);

    private static string FormatUniqueTarget(string schema, string table, string column)
        => $"{schema}.{table}.{column}";

    private static string FormatSafeScriptLine(string? path, int readyOpportunities)
    {
        var formattedPath = FormatPath(path);
        if (readyOpportunities <= 0)
        {
            return $"  Safe script: {formattedPath}";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "  Safe script: {0} ({1} ready)",
            formattedPath,
            readyOpportunities);
    }

    private static string FormatValidationsLine(string? path, int validationCount)
    {
        var formattedPath = FormatPath(path);
        if (validationCount <= 0)
        {
            return $"  Validations: {formattedPath}";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "  Validations: {0} ({1} confirmed)",
            formattedPath,
            validationCount);
    }

    private static string FormatRemediationScriptLine(string? path, int contradictionCount)
    {
        var formattedPath = FormatPath(path);
        if (contradictionCount <= 0)
        {
            return $"  Remediation script: {formattedPath}";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "  Remediation script: {0} (⚠️ {1} contradictions)",
            formattedPath,
            contradictionCount);
    }

    private static string FormatPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? "(not emitted)" : path;

    private static void EmitNullViolationSamples(IConsole console, ProfileSnapshot snapshot)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var samples = snapshot.Columns
            .Where(static column => !column.IsNullablePhysical
                && column.NullCount > 0
                && column.NullRowSample is { SampleRows.Length: > 0 })
            .OrderByDescending(static column => column.NullCount)
            .ToList();

        if (samples.Count == 0)
        {
            return;
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "  Sample rows violating NOT NULL:");

        foreach (var column in samples)
        {
            var sample = column.NullRowSample!;
            WriteLine(
                console,
                $"    {FormatColumnCoordinate(column)} {FormatSampleSummary(sample.SampleRows.Length, sample.TotalNullRows, sample.IsTruncated)}");

            foreach (var row in sample.SampleRows)
            {
                WriteLine(console, $"      {row}");
            }
        }
    }

    private static void EmitForeignKeyOrphanSamples(IConsole console, ProfileSnapshot snapshot)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var samples = snapshot.ForeignKeys
            .Where(static fk => fk.HasOrphan && fk.OrphanSample is { SampleRows.Length: > 0 })
            .OrderByDescending(static fk => fk.OrphanCount)
            .ToList();

        if (samples.Count == 0)
        {
            return;
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "  Foreign key orphan samples:");

        foreach (var foreignKey in samples)
        {
            var sample = foreignKey.OrphanSample!;
            WriteLine(
                console,
                $"    {FormatForeignKeyReference(foreignKey.Reference)} {FormatSampleSummary(sample.SampleRows.Length, sample.TotalOrphans, sample.IsTruncated)}");

            foreach (var row in sample.SampleRows)
            {
                WriteLine(console, $"      {row}");
            }
        }
    }

    private static string FormatSampleSummary(int displayedCount, long totalCount, bool isTruncated)
    {
        var suffix = isTruncated ? ", truncated" : string.Empty;
        return string.Format(
            CultureInfo.InvariantCulture,
            "(showing {0} of {1:N0}{2})",
            displayedCount,
            totalCount,
            suffix);
    }

    private static string FormatForeignKeyReference(ForeignKeyReference reference)
    {
        if (reference is null)
        {
            return string.Empty;
        }

        var from = $"{reference.FromSchema.Value}.{reference.FromTable.Value}.{reference.FromColumn.Value}";
        var to = $"{reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value}";
        return $"{from} -> {to}";
    }

    private static string FormatColumnCoordinate(ColumnProfile column)
        => column is null
            ? string.Empty
            : $"{column.Schema.Value}.{column.Table.Value}.{column.Column.Value}";

    private static string FormatPercentage(long count, long total)
    {
        if (total <= 0)
        {
            return "0.00%";
        }

        var ratio = (double)count / total;
        return (ratio * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatColumnFlags(ColumnProfile column)
    {
        if (column is null)
        {
            return string.Empty;
        }

        var flags = new List<string>();

        if (column.IsPrimaryKey)
        {
            flags.Add("PK");
        }

        if (column.IsUniqueKey)
        {
            flags.Add("Unique");
        }

        if (column.IsComputed)
        {
            flags.Add("Computed");
        }

        if (!string.IsNullOrWhiteSpace(column.DefaultDefinition))
        {
            flags.Add("Default");
        }

        return flags.Count == 0 ? "--" : string.Join(", ", flags);
    }

    private static string FormatProbeStatus(ProfilingProbeStatus status)
    {
        var sample = status.SampleSize.ToString("N0", CultureInfo.InvariantCulture);

        return status.Outcome switch
        {
            ProfilingProbeOutcome.Succeeded => $"OK (sample {sample})",
            ProfilingProbeOutcome.FallbackTimeout => $"Timeout (sample {sample})",
            ProfilingProbeOutcome.Cancelled => $"Cancelled (sample {sample})",
            _ => $"Unknown (sample {sample})"
        };
    }

    private static string GetProbeIssueDetail(ProfilingProbeStatus status)
        => status.Outcome switch
        {
            ProfilingProbeOutcome.FallbackTimeout => "Sampling timed out before completion",
            ProfilingProbeOutcome.Cancelled => "Sampling cancelled before completion",
            ProfilingProbeOutcome.Unknown => "Sampling outcome unknown",
            _ => "Probe completed successfully"
        };

    private static void EmitTableOverflow(IConsole console, int totalCount, int displayedCount)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (totalCount > displayedCount)
        {
            WriteLine(console, $"  … {totalCount - displayedCount} additional item(s) truncated.");
        }
    }

    private static string FormatIssueSeverity(IssueSeverity severity)
        => severity switch
        {
            IssueSeverity.Critical => "CRITICAL",
            IssueSeverity.Warning => "WARNING",
            _ => "INFO"
        };

    private static string FormatIssueSeverityCounts(IEnumerable<IssueSeverity> severities)
    {
        if (severities is null)
        {
            return "no actionable issues";
        }

        var counts = new Dictionary<IssueSeverity, int>();

        foreach (var severity in severities)
        {
            if (!counts.TryAdd(severity, 1))
            {
                counts[severity]++;
            }
        }

        var parts = new List<string>();

        if (counts.TryGetValue(IssueSeverity.Critical, out var critical) && critical > 0)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} critical", critical));
        }

        if (counts.TryGetValue(IssueSeverity.Warning, out var warning) && warning > 0)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} warning", warning));
        }

        if (counts.TryGetValue(IssueSeverity.Info, out var info) && info > 0)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} informational", info));
        }

        return parts.Count > 0
            ? string.Join(", ", parts)
            : "no actionable issues";
    }

    public static void EmitMultiEnvironmentReport(IConsole console, MultiEnvironmentProfileReport? report)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (report is null || report.Environments.IsDefaultOrEmpty || report.Environments.Length == 0)
        {
            return;
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "Multi-environment profiling summary:");

        var headers = ImmutableArray.Create(
            "Environment",
            "Role",
            "Label Source",
            "Columns",
            "Null cols",
            "Null probes",
            "Unique dupes",
            "FK orphans",
            "FK probes",
            "No-check",
            "Duration");

        var rows = report.Environments
            .Select(summary => (summary, Row: ImmutableArray.Create(
                summary.IsPrimary ? $"⭐ {summary.Name}" : summary.Name,
                summary.IsPrimary ? "Primary" : "Secondary",
                DescribeLabelOrigin(summary.LabelOrigin, summary.LabelWasAdjusted),
                summary.ColumnCount.ToString(CultureInfo.InvariantCulture),
                summary.ColumnsWithNulls.ToString(CultureInfo.InvariantCulture),
                summary.ColumnsWithUnknownNullStatus.ToString(CultureInfo.InvariantCulture),
                summary.UniqueViolations.ToString(CultureInfo.InvariantCulture),
                summary.ForeignKeyOrphans.ToString(CultureInfo.InvariantCulture),
                summary.ForeignKeyProbeUnknown.ToString(CultureInfo.InvariantCulture),
                summary.ForeignKeyNoCheck.ToString(CultureInfo.InvariantCulture),
                FormatDuration(summary.Duration))))
            .Select(tuple => (IReadOnlyList<string>)tuple.Row)
            .ToImmutableArray();

        WriteTable(console, headers, rows);

        EmitEnvironmentReadinessDigest(console, report.Environments);

        if (!report.Findings.IsDefaultOrEmpty && report.Findings.Length > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "Environment findings:");
            foreach (var finding in report.Findings)
            {
                if (finding is null)
                {
                    continue;
                }

                var icon = GetFindingIcon(finding.Severity);
                WriteLine(console, $"  {icon} [{finding.Severity}] {finding.Title}");
                WriteLine(console, $"      Summary: {finding.Summary}");
                if (!string.IsNullOrWhiteSpace(finding.SuggestedAction))
                {
                    WriteLine(console, $"      Action: {finding.SuggestedAction}");
                }
                if (!finding.AffectedObjects.IsDefaultOrEmpty && finding.AffectedObjects.Length > 0)
                {
                    foreach (var affected in finding.AffectedObjects)
                    {
                        if (!string.IsNullOrWhiteSpace(affected))
                        {
                            WriteLine(console, $"        - {affected}");
                        }
                    }
                }
            }
        }

        EmitConstraintConsensus(console, report.ConstraintConsensus);
    }

    private static void EmitConstraintConsensus(IConsole console, MultiEnvironmentConstraintConsensus? consensus)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (consensus is null)
        {
            return;
        }

        var results = EnumerateConsensusResults(consensus)
            .ToList();

        if (results.Count == 0)
        {
            return;
        }

        var statistics = consensus.Statistics;

        WriteLine(console, string.Empty);
        WriteLine(console, "Constraint consensus across environments:");
        WriteLine(console, $"  {statistics.FormatSummary()}");
        WriteLine(
            console,
            string.Format(
                CultureInfo.InvariantCulture,
                "  NOT NULL: {0} safe / {1} risky | UNIQUE: {2} safe / {3} risky | FOREIGN KEY: {4} safe / {5} risky",
                statistics.SafeNotNullConstraints,
                statistics.UnsafeNotNullConstraints,
                statistics.SafeUniqueConstraints,
                statistics.UnsafeUniqueConstraints,
                statistics.SafeForeignKeyConstraints,
                statistics.UnsafeForeignKeyConstraints));

        EmitConstraintReadinessDigest(console, results);

        var safeResults = results
            .Where(static result => result.IsSafeToApply)
            .OrderByDescending(static result => result.ConsensusRatio)
            .ThenBy(static result => DescribeConstraintType(result.ConstraintType), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static result => result.ConstraintDescriptor, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (safeResults.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "Constraints safe across all environments:");

            var rows = BuildConsensusRows(safeResults, includeGuidance: true);
            var headers = ImmutableArray.Create("Type", "Constraint", "Consensus", "Guidance");

            WriteTable(console, headers, rows);
            EmitTableOverflow(console, safeResults.Count, rows.Length);
        }

        var unsafeResults = results
            .Where(static result => !result.IsSafeToApply)
            .OrderBy(static result => result.ConsensusRatio)
            .ThenBy(static result => DescribeConstraintType(result.ConstraintType), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static result => result.ConstraintDescriptor, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unsafeResults.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "Constraints requiring remediation before DDL enforcement:");

            var rows = BuildConsensusRows(unsafeResults, includeGuidance: true);
            var headers = ImmutableArray.Create("Type", "Constraint", "Consensus", "Guidance");

            WriteTable(console, headers, rows);
            EmitTableOverflow(console, unsafeResults.Count, rows.Length);
        }
        else if (safeResults.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "All analyzed constraints are ready for multi-environment DDL application.");
        }
    }

    private static void EmitEnvironmentReadinessDigest(
        IConsole console,
        ImmutableArray<ProfilingEnvironmentSummary> environments)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (environments.IsDefaultOrEmpty || environments.Length < 2)
        {
            return;
        }

        var primary = environments.FirstOrDefault(static summary => summary is not null && summary.IsPrimary)
            ?? environments[0];

        var entries = new List<string>();

        foreach (var summary in environments)
        {
            if (summary is null || string.Equals(summary.Name, primary.Name, StringComparison.Ordinal))
            {
                continue;
            }

            var reasons = new List<string>();

            if (summary.UniqueViolations > primary.UniqueViolations)
            {
                reasons.Add("uniqueness drift");
            }

            if (summary.ForeignKeyOrphans > primary.ForeignKeyOrphans)
            {
                reasons.Add("foreign key orphans");
            }

            if (summary.ForeignKeyProbeUnknown > primary.ForeignKeyProbeUnknown)
            {
                reasons.Add("probe gaps");
            }

            if (reasons.Count == 0)
            {
                continue;
            }

            var reasonText = string.Join(", ", reasons);
            entries.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Review {0} data quality ({1}).",
                summary.Name,
                reasonText));
        }

        if (entries.Count == 0)
        {
            return;
        }

        WriteLine(console, string.Empty);
        WriteLine(console, "Multi-environment readiness digest:");

        var displayed = 0;
        foreach (var entry in entries.Take(DefaultTableLimit))
        {
            WriteLine(console, $"  - {entry}");
            displayed++;
        }

        EmitTableOverflow(console, entries.Count, displayed);
    }

    private static void EmitConstraintReadinessDigest(
        IConsole console,
        IReadOnlyList<ConstraintConsensusResult> results)
    {
        if (results is null || results.Count == 0)
        {
            return;
        }

        var blockedGroups = results
            .Where(static result => !result.IsSafeToApply)
            .GroupBy(static result => result.ConstraintType)
            .OrderByDescending(static group => group.Count())
            .ToList();

        WriteLine(console, string.Empty);

        if (blockedGroups.Count > 0)
        {
            WriteLine(console, "DDL readiness blockers:");

            foreach (var group in blockedGroups)
            {
                var top = group
                    .OrderBy(static result => result.ConsensusRatio)
                    .ThenBy(static result => result.ConstraintDescriptor, StringComparer.OrdinalIgnoreCase)
                    .First();

                WriteLine(
                    console,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "  - {0}: {1} blocked. Worst case {2} – {3}. {4}",
                        DescribeConstraintType(group.Key),
                        group.Count(),
                        FormatConsensus(top),
                        top.ConstraintDescriptor,
                        top.Recommendation));
            }
        }
        else
        {
            WriteLine(console, "DDL readiness blockers: none detected.");
        }

        var partialConsensus = results
            .Where(static result => result.IsSafeToApply && result.ConsensusRatio < 1.0)
            .OrderBy(static result => result.ConsensusRatio)
            .ThenBy(static result => result.ConstraintDescriptor, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (partialConsensus.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "Watchlist (safe under configured threshold but not unanimous):");

            var displayed = 0;
            foreach (var result in partialConsensus.Take(DefaultTableLimit))
            {
                WriteLine(
                    console,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "  - {0} {1}: {2}. {3}",
                        DescribeConstraintType(result.ConstraintType),
                        result.ConstraintDescriptor,
                        FormatConsensus(result),
                        result.Recommendation));
                displayed++;
            }

            EmitTableOverflow(console, partialConsensus.Count, displayed);
        }
    }

    private static ImmutableArray<IReadOnlyList<string>> BuildConsensusRows(
        IReadOnlyList<ConstraintConsensusResult> results,
        bool includeGuidance)
    {
        var rows = results
            .Take(DefaultTableLimit)
            .Select(result => (IReadOnlyList<string>)ImmutableArray.Create(
                DescribeConstraintType(result.ConstraintType),
                result.ConstraintDescriptor,
                FormatConsensus(result),
                includeGuidance ? result.Recommendation : string.Empty))
            .ToImmutableArray();

        return rows;
    }

    private static IEnumerable<ConstraintConsensusResult> EnumerateConsensusResults(
        MultiEnvironmentConstraintConsensus consensus)
    {
        foreach (var result in consensus.NullabilityConsensus)
        {
            if (result is not null)
            {
                yield return result;
            }
        }

        foreach (var result in consensus.UniqueConstraintConsensus)
        {
            if (result is not null)
            {
                yield return result;
            }
        }

        foreach (var result in consensus.ForeignKeyConsensus)
        {
            if (result is not null)
            {
                yield return result;
            }
        }
    }

    private static string DescribeConstraintType(ConstraintType constraintType)
        => constraintType switch
        {
            ConstraintType.NotNull => "NOT NULL",
            ConstraintType.Unique => "UNIQUE",
            ConstraintType.CompositeUnique => "UNIQUE (composite)",
            ConstraintType.ForeignKey => "FOREIGN KEY",
            _ => constraintType.ToString().ToUpperInvariant()
        };

    private static string FormatConsensus(ConstraintConsensusResult result)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1} ({2:P0})",
            result.SafeEnvironmentCount,
            result.TotalEnvironmentCount,
            result.ConsensusRatio);
    }

    private static string DescribeLabelOrigin(
        MultiTargetSqlDataProfiler.EnvironmentLabelOrigin origin,
        bool labelWasAdjusted)
    {
        var description = origin switch
        {
            MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided => "Provided",
            MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.DerivedFromDatabase => "Derived (database)",
            MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.DerivedFromApplicationName => "Derived (application)",
            MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.DerivedFromDataSource => "Derived (data source)",
            _ => "Fallback"
        };

        if (labelWasAdjusted)
        {
            description += " +dedup";
        }

        return description;
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

        var errors = insights.Count(static i => i?.Severity == ProfilingInsightSeverity.Error);
        var warnings = insights.Count(static i => i?.Severity == ProfilingInsightSeverity.Warning);
        var informational = insights.Length - errors - warnings;

        WriteLine(
            console,
            $"Profiling insights: {insights.Length} total ({errors} errors, {warnings} warnings, {informational} informational)");

        var highSeverity = insights
            .Where(static i => i is { Severity: ProfilingInsightSeverity.Error or ProfilingInsightSeverity.Warning })
            .OrderByDescending(static i => i!.Severity)
            .ThenBy(static i => i!.Category)
            .ThenBy(static i => FormatProfilingInsightCoordinate(i!.Coordinate), StringComparer.OrdinalIgnoreCase)
            .OfType<ProfilingInsight>()
            .ToList();

        if (highSeverity.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "High severity insights:");

            var rows = highSeverity
                .Take(DefaultTableLimit)
                .Select(insight => (IReadOnlyList<string>)ImmutableArray.Create(
                    insight.Severity.ToString(),
                    insight.Category.ToString(),
                    FormatProfilingInsightCoordinate(insight.Coordinate),
                    insight.Message))
                .ToImmutableArray();

            var headers = ImmutableArray.Create("Severity", "Category", "Location", "Message");
            WriteTable(console, headers, rows);
            EmitTableOverflow(console, highSeverity.Count, rows.Length);
        }

        var recommendations = insights
            .Where(static i => i is { Severity: ProfilingInsightSeverity.Recommendation })
            .OfType<ProfilingInsight>()
            .OrderBy(static i => FormatProfilingInsightCoordinate(i.Coordinate), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recommendations.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "Recommendations:");

            var rows = recommendations
                .Take(DefaultTableLimit)
                .Select(insight => (IReadOnlyList<string>)ImmutableArray.Create(
                    insight.Category.ToString(),
                    FormatProfilingInsightCoordinate(insight.Coordinate),
                    insight.Message))
                .ToImmutableArray();

            var headers = ImmutableArray.Create("Category", "Location", "Message");
            WriteTable(console, headers, rows);
            EmitTableOverflow(console, recommendations.Count, rows.Length);
        }

        var infoInsights = insights
            .Where(static i => i is { Severity: ProfilingInsightSeverity.Info })
            .OfType<ProfilingInsight>()
            .OrderBy(static i => FormatProfilingInsightCoordinate(i.Coordinate), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (infoInsights.Count > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, "Informational insights:");

            var rows = infoInsights
                .Take(DefaultTableLimit)
                .Select(insight => (IReadOnlyList<string>)ImmutableArray.Create(
                    insight.Category.ToString(),
                    FormatProfilingInsightCoordinate(insight.Coordinate),
                    insight.Message))
                .ToImmutableArray();

            var headers = ImmutableArray.Create("Category", "Location", "Message");
            WriteTable(console, headers, rows);
            EmitTableOverflow(console, infoInsights.Count, rows.Length);
        }
    }

    public static void EmitContradictionDetails(IConsole console, Osm.Validation.Tightening.Opportunities.OpportunitiesReport opportunities)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (opportunities is null || opportunities.ContradictionCount == 0)
        {
            return;
        }

        var contradictions = opportunities.Opportunities
            .Where(o => o.Category == OpportunityCategory.Contradiction)
            .ToArray();

        if (contradictions.Length == 0)
        {
            return;
        }

        WriteLine(console, string.Empty);
        WriteErrorLine(console, "═══════════════════════════════════════════════════════════════════════════════");
        WriteErrorLine(console, "⚠️  DATA CONTRADICTIONS DETECTED - MANUAL REMEDIATION REQUIRED");
        WriteErrorLine(console, "═══════════════════════════════════════════════════════════════════════════════");
        WriteLine(console, string.Empty);

        // Group by severity (based on row count)
        var bySeverity = contradictions
            .Select(c => (
                Opportunity: c,
                RowCount: c.Columns.FirstOrDefault()?.RowCount ?? 0,
                NullCount: c.Columns.FirstOrDefault()?.NullCount ?? 0
            ))
            .OrderByDescending(x => x.RowCount)
            .ThenByDescending(x => x.NullCount)
            .ToArray();

        foreach (var item in bySeverity)
        {
            var opp = item.Opportunity;
            var column = opp.Columns.FirstOrDefault();

            if (column is null)
            {
                continue;
            }

            var severity = GetSeverityLabel(item.RowCount, item.NullCount);
            WriteErrorLine(console, $"[{severity}] {opp.Type}: {column.Module}.{column.Entity}.{column.Attribute}");

            if (item.RowCount > 0)
            {
                WriteErrorLine(console, $"  Total Rows: {item.RowCount:N0}");
            }

            if (opp.Type == OpportunityType.Nullability && item.NullCount > 0)
            {
                var percentage = item.RowCount > 0 ? (item.NullCount * 100.0 / item.RowCount) : 0;
                WriteErrorLine(console, $"  NULL Values: {item.NullCount:N0} ({percentage:F2}% of total)");
            }

            if (column.HasDuplicates == true)
            {
                WriteErrorLine(console, "  Issue: Duplicate values detected in unique index");
            }

            if (column.HasOrphans == true)
            {
                WriteErrorLine(console, "  Issue: Orphaned rows violate referential integrity");
            }

            WriteErrorLine(console, $"  Location: {column.Coordinate.Schema.Value}.{column.Coordinate.Table.Value}.{column.Coordinate.Column.Value}");
            WriteErrorLine(console, $"  Summary: {opp.Summary}");

            if (!opp.Evidence.IsDefaultOrEmpty)
            {
                WriteErrorLine(console, "  Evidence:");
                foreach (var evidence in opp.Evidence.Take(3))
                {
                    WriteErrorLine(console, $"    - {evidence}");
                }

                if (opp.Evidence.Length > 3)
                {
                    WriteErrorLine(console, $"    ... {opp.Evidence.Length - 3} more evidence item(s)");
                }
            }

            WriteLine(console, string.Empty);
        }

        WriteLine(console, $"Review the remediation script for details: {opportunities.TotalCount} total opportunities");
        WriteLine(console, string.Empty);
    }

    private static string GetSeverityLabel(long rowCount, long nullCount)
    {
        if (rowCount == 0)
        {
            return "UNKNOWN";
        }

        var percentage = nullCount * 100.0 / rowCount;

        if (percentage >= 50 || nullCount >= 10000)
        {
            return "CRITICAL";
        }

        if (percentage >= 10 || nullCount >= 1000)
        {
            return "HIGH";
        }

        if (percentage >= 1 || nullCount >= 100)
        {
            return "MODERATE";
        }

        return "LOW";
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

        WriteLine(console, "Module summary:");
        foreach (var module in modules)
        {
            if (!manifestRollups.TryGetValue(module, out var manifest))
            {
                manifest = ModuleManifestRollup.Empty;
            }

            decisionRollups.TryGetValue(module, out var decision);

            WriteLine(console, $"  {module}:");
            WriteLine(console, $"    Tables: {manifest.TableCount:N0}, Indexes: {manifest.IndexCount:N0}, Foreign Keys: {manifest.ForeignKeyCount:N0}");

            if (decision is not null)
            {
                var tighteningInfo = new List<string>
                {
                    $"Columns: {decision.ColumnCount:N0} total, {decision.TightenedColumnCount:N0} confirmed NOT NULL"
                };

                if (decision.RemediationColumnCount > 0)
                {
                    tighteningInfo.Add($"{decision.RemediationColumnCount:N0} need remediation");
                }

                WriteLine(console, $"    {string.Join(", ", tighteningInfo)}");

                if (decision.UniqueIndexesEnforcedCount > 0 || decision.UniqueIndexesRequireRemediationCount > 0)
                {
                    WriteLine(console, $"    Unique Indexes: {decision.UniqueIndexesEnforcedCount:N0} confirmed UNIQUE, {decision.UniqueIndexesRequireRemediationCount:N0} need remediation");
                }

                if (decision.ForeignKeysCreatedCount > 0)
                {
                    WriteLine(console, $"    Foreign Keys: {decision.ForeignKeysCreatedCount:N0} safe to create");
                }
            }
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

    private static string FormatTableRow(IReadOnlyList<string> values, int[] widths, bool[] numericColumns)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            var value = values[i] ?? string.Empty;
            var width = i < widths.Length ? widths[i] : value.Length;
            var rightAlign = i < numericColumns.Length && numericColumns[i];
            builder.Append(rightAlign ? value.PadLeft(width) : value.PadRight(width));
        }

        return builder.ToString();
    }

    private static string FormatTableSeparator(int[] widths)
    {
        var segments = widths.Select(width => new string('-', Math.Max(width, 1)));
        return string.Join("-+-", segments);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        if (duration.TotalMinutes >= 1)
        {
            return duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }

        return duration.ToString(@"ss\.fff\ s", CultureInfo.InvariantCulture);
    }

    private static string GetFindingIcon(MultiEnvironmentFindingSeverity severity)
        => severity switch
        {
            MultiEnvironmentFindingSeverity.Critical => "🚨",
            MultiEnvironmentFindingSeverity.Warning => "⚠️ ",
            MultiEnvironmentFindingSeverity.Advisory => "💡 ",
            _ => "ℹ️ "
        };

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
                builder.Append(" | ");
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
                builder.Append("-+-");
            }

            var dashCount = Math.Max(3, widths[i]);
            builder.Append(new string('-', dashCount));
        }

        return builder.ToString();
    }

    public static void EmitTighteningStatisticsDetails(
        IConsole console,
        PolicyDecisionReport report)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        const int MaxSamples = 3;

        // Columns NOT confirmed as NOT NULL
        var columnsNotTightened = report.Columns.Where(c => !c.MakeNotNull).ToArray();
        if (columnsNotTightened.Length > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, $"Columns not confirmed as NOT NULL: {columnsNotTightened.Length}");

            var reasonGroups = columnsNotTightened
                .SelectMany(c => c.Rationales.Select(r => (Rationale: r, Column: c)))
                .GroupBy(x => x.Rationale)
                .OrderByDescending(g => g.Count())
                .ToArray();

            foreach (var group in reasonGroups)
            {
                var rationale = FormatRationale(group.Key);
                var samples = group.Take(MaxSamples)
                    .Select(x => $"{x.Column.Column.Schema}.{x.Column.Column.Table}.{x.Column.Column.Column}")
                    .ToArray();

                WriteLine(console, $"  {rationale}: {group.Count()}");
                foreach (var sample in samples)
                {
                    WriteLine(console, $"    - {sample}");
                }
                if (group.Count() > MaxSamples)
                {
                    WriteLine(console, $"    ... and {group.Count() - MaxSamples} more");
                }
            }
        }

        // Unique indexes NOT confirmed as UNIQUE
        var uniqueNotEnforced = report.UniqueIndexes.Where(u => !u.EnforceUnique).ToArray();
        if (uniqueNotEnforced.Length > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, $"Unique indexes not confirmed as UNIQUE: {uniqueNotEnforced.Length}");

            var reasonGroups = uniqueNotEnforced
                .SelectMany(u => u.Rationales.Select(r => (Rationale: r, Index: u)))
                .GroupBy(x => x.Rationale)
                .OrderByDescending(g => g.Count())
                .ToArray();

            foreach (var group in reasonGroups)
            {
                var rationale = FormatRationale(group.Key);
                var samples = group.Take(MaxSamples)
                    .Select(x => $"{x.Index.Index.Schema}.{x.Index.Index.Table}.{x.Index.Index.Index}")
                    .ToArray();

                WriteLine(console, $"  {rationale}: {group.Count()}");
                foreach (var sample in samples)
                {
                    WriteLine(console, $"    - {sample}");
                }
                if (group.Count() > MaxSamples)
                {
                    WriteLine(console, $"    ... and {group.Count() - MaxSamples} more");
                }
            }
        }

        // Foreign keys NOT safe to create
        var fkNotCreated = report.ForeignKeys.Where(fk => !fk.CreateConstraint).ToArray();
        if (fkNotCreated.Length > 0)
        {
            WriteLine(console, string.Empty);
            WriteLine(console, $"Foreign key constraints not safe to create: {fkNotCreated.Length}");

            var reasonGroups = fkNotCreated
                .SelectMany(fk => fk.Rationales.Select(r => (Rationale: r, ForeignKey: fk)))
                .GroupBy(x => x.Rationale)
                .OrderByDescending(g => g.Count())
                .ToArray();

            foreach (var group in reasonGroups)
            {
                var rationale = FormatRationale(group.Key);
                var samples = group.Take(MaxSamples)
                    .Select(x => $"{x.ForeignKey.Column.Schema}.{x.ForeignKey.Column.Table}.{x.ForeignKey.Column.Column}")
                    .ToArray();

                WriteLine(console, $"  {rationale}: {group.Count()}");
                foreach (var sample in samples)
                {
                    WriteLine(console, $"    - {sample}");
                }
                if (group.Count() > MaxSamples)
                {
                    WriteLine(console, $"    ... and {group.Count() - MaxSamples} more");
                }
            }
        }
    }

    private static string FormatRationale(string rationale)
    {
        // Convert constant names to readable messages
        return rationale switch
        {
            TighteningRationales.DataHasNulls => "Data contains NULL values",
            TighteningRationales.NullBudgetEpsilon => "NULL values within acceptable budget",
            TighteningRationales.RemediateBeforeTighten => "Requires remediation before tightening",
            TighteningRationales.ProfileMissing => "No profiling data available",
            TighteningRationales.UniqueDuplicatesPresent => "Duplicate values found",
            TighteningRationales.CompositeUniqueDuplicatesPresent => "Composite duplicate values found",
            TighteningRationales.UniquePolicyDisabled => "Unique constraint policy disabled",
            TighteningRationales.DataHasOrphans => "Orphaned references found in data",
            TighteningRationales.DeleteRuleIgnore => "Delete rule set to IGNORE",
            TighteningRationales.CrossSchema => "Cross-schema reference not supported",
            TighteningRationales.CrossCatalog => "Cross-catalog reference not supported",
            TighteningRationales.ForeignKeyCreationDisabled => "Foreign key creation disabled by policy",
            _ => rationale
        };
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

            // Only emit template for unresolved or conflicting duplicates
            // Resolved duplicates are already handled via namingOverrides.rules
            if (!diagnostic.Code.StartsWith("tightening.entity.duplicate", StringComparison.Ordinal) ||
                diagnostic.Code.Equals("tightening.entity.duplicate.resolved", StringComparison.Ordinal))
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
