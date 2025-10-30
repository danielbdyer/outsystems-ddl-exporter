using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Emission;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class PolicyDecisionLogWriter : IPolicyDecisionLogWriter
{
    public async Task<Result<string>> WriteAsync(
        string outputDirectory,
        PolicyDecisionReport report,
        CancellationToken cancellationToken = default,
        SsdtPredicateCoverage? predicateCoverage = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var moduleRollups = report.ModuleRollups.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var togglePrecedence = report.TogglePrecedence;

        var log = new PolicyDecisionLog(
            report.ColumnCount,
            report.TightenedColumnCount,
            report.RemediationColumnCount,
            report.UniqueIndexCount,
            report.UniqueIndexesEnforcedCount,
            report.UniqueIndexesRequireRemediationCount,
            report.ForeignKeyCount,
            report.ForeignKeysCreatedCount,
            report.ColumnRationaleCounts,
            report.UniqueIndexRationaleCounts,
            report.ForeignKeyRationaleCounts,
            moduleRollups,
            togglePrecedence,
            report.Columns.Select(static c => new PolicyDecisionLogColumn(
                c.Module,
                c.Column.Schema.Value,
                c.Column.Table.Value,
                c.Column.Column.Value,
                c.MakeNotNull,
                c.RequiresRemediation,
                c.Rationales.ToArray())).ToArray(),
            report.UniqueIndexes.Select(static u => new PolicyDecisionLogUniqueIndex(
                u.Module,
                u.Index.Schema.Value,
                u.Index.Table.Value,
                u.Index.Index.Value,
                u.EnforceUnique,
                u.RequiresRemediation,
                u.Rationales.ToArray())).ToArray(),
            report.ForeignKeys.Select(static f => new PolicyDecisionLogForeignKey(
                f.Module,
                f.Column.Schema.Value,
                f.Column.Table.Value,
                f.Column.Column.Value,
                f.CreateConstraint,
                f.Rationales.ToArray())).ToArray(),
            report.Diagnostics.Select(static d => new PolicyDecisionLogDiagnostic(
                d.LogicalName,
                d.CanonicalModule,
                d.CanonicalSchema,
                d.CanonicalPhysicalName,
                d.Code,
                d.Message,
                d.Severity.ToString(),
                d.ResolvedByOverride,
                d.Candidates.Select(static c => new PolicyDecisionLogDuplicateCandidate(
                    c.Module,
                    c.Schema,
                    c.PhysicalName)).ToArray())).ToArray(),
            predicateCoverage ?? SsdtPredicateCoverage.Empty);

        var logPath = Path.Combine(outputDirectory, "policy-decisions.json");
        var reportPath = Path.Combine(outputDirectory, "policy-decisions.report.json");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var logJson = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(logPath, logJson, cancellationToken).ConfigureAwait(false);

            var reportJson = JsonSerializer.Serialize(report, PolicyDecisionReportJson.GetSerializerOptions());
            await File.WriteAllTextAsync(reportPath, reportJson, cancellationToken).ConfigureAwait(false);

            return Result<string>.Success(logPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.output.permissionDenied",
                $"Failed to write policy decision artifacts to '{outputDirectory}': {ex.Message}"));
        }
    }

    private sealed record PolicyDecisionLog(
        int ColumnCount,
        int TightenedColumnCount,
        int RemediationColumnCount,
        int UniqueIndexCount,
        int UniqueIndexesEnforcedCount,
        int UniqueIndexesRequireRemediationCount,
        int ForeignKeyCount,
        int ForeignKeysCreatedCount,
        IReadOnlyDictionary<string, int> ColumnRationales,
        IReadOnlyDictionary<string, int> UniqueIndexRationales,
        IReadOnlyDictionary<string, int> ForeignKeyRationales,
        IReadOnlyDictionary<string, ModuleDecisionRollup> ModuleRollups,
        IReadOnlyDictionary<string, ToggleExportValue> TogglePrecedence,
        IReadOnlyList<PolicyDecisionLogColumn> Columns,
        IReadOnlyList<PolicyDecisionLogUniqueIndex> UniqueIndexes,
        IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys,
        IReadOnlyList<PolicyDecisionLogDiagnostic> Diagnostics,
        SsdtPredicateCoverage PredicateCoverage);

    private sealed record PolicyDecisionLogColumn(
        string? Module,
        string Schema,
        string Table,
        string Column,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogUniqueIndex(
        string? Module,
        string Schema,
        string Table,
        string Index,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogForeignKey(
        string? Module,
        string Schema,
        string Table,
        string Column,
        bool CreateConstraint,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogDiagnostic(
        string LogicalName,
        string CanonicalModule,
        string CanonicalSchema,
        string CanonicalPhysicalName,
        string Code,
        string Message,
        string Severity,
        bool ResolvedByOverride,
        IReadOnlyList<PolicyDecisionLogDuplicateCandidate> Candidates);

    private sealed record PolicyDecisionLogDuplicateCandidate(string Module, string Schema, string PhysicalName);
}
