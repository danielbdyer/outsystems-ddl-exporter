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
            report.Columns.Select(c => new PolicyDecisionLogColumn(
                c.Column.Schema.Value,
                c.Column.Table.Value,
                c.Column.Column.Value,
                c.MakeNotNull,
                c.RequiresRemediation,
                c.Rationales.ToArray(),
                report.ColumnModules.TryGetValue(c.Column.ToString(), out var columnModule) ? columnModule : string.Empty)).ToArray(),
            report.UniqueIndexes.Select(u => new PolicyDecisionLogUniqueIndex(
                u.Index.Schema.Value,
                u.Index.Table.Value,
                u.Index.Index.Value,
                u.EnforceUnique,
                u.RequiresRemediation,
                u.Rationales.ToArray(),
                report.IndexModules.TryGetValue(u.Index.ToString(), out var indexModule) ? indexModule : string.Empty)).ToArray(),
            report.ForeignKeys.Select(f => new PolicyDecisionLogForeignKey(
                f.Column.Schema.Value,
                f.Column.Table.Value,
                f.Column.Column.Value,
                f.CreateConstraint,
                f.ScriptWithNoCheck,
                f.Rationales.ToArray(),
                report.ColumnModules.TryGetValue(f.Column.ToString(), out var foreignKeyModule) ? foreignKeyModule : string.Empty)).ToArray(),
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

        var path = Path.Combine(outputDirectory, "policy-decisions.json");
        var reportPath = Path.Combine(outputDirectory, "policy-decision-report.json");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(log, serializerOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
            var reportJson = JsonSerializer.Serialize(report, serializerOptions);
            await File.WriteAllTextAsync(reportPath, reportJson, cancellationToken).ConfigureAwait(false);
            return Result<string>.Success(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.output.permissionDenied",
                $"Failed to write policy decision log to '{outputDirectory}': {ex.Message}"));
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
        string Schema,
        string Table,
        string Column,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales,
        string Module);

    private sealed record PolicyDecisionLogUniqueIndex(
        string Schema,
        string Table,
        string Index,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales,
        string Module);

    private sealed record PolicyDecisionLogForeignKey(
        string Schema,
        string Table,
        string Column,
        bool CreateConstraint,
        bool ScriptWithNoCheck,
        IReadOnlyList<string> Rationales,
        string Module);

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
