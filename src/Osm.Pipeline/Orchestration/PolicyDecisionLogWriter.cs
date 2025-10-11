using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class PolicyDecisionLogWriter
{
    private const string SchemaVersion = "1.0.0";

    public async Task<string> WriteAsync(
        string outputDirectory,
        PolicyDecisionReport report,
        IEnumerable<PolicyWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        var warningArray = warnings.ToImmutableArray();
        var log = new PolicyDecisionLog(
            SchemaVersion,
            DateTime.UtcNow,
            new PolicyDecisionLogSummary(
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
                report.ForeignKeyRationaleCounts),
            warningArray.Select(static w => new PolicyDecisionLogWarning(
                w.Code,
                w.Message,
                w.Evidence.Select(ToEvidence).ToArray())).ToArray(),
            report.Columns.Select(static c => new PolicyDecisionLogColumn(
                c.Column.Schema.Value,
                c.Column.Table.Value,
                c.Column.Column.Value,
                c.RuleId,
                c.MakeNotNull,
                c.RequiresRemediation,
                c.Rationales.ToArray(),
                c.PreRemediationSql.ToArray(),
                c.Evidence.Select(ToEvidence).ToArray())).ToArray(),
            report.UniqueIndexes.Select(static u => new PolicyDecisionLogUniqueIndex(
                u.Index.Schema.Value,
                u.Index.Table.Value,
                u.Index.Index.Value,
                u.RuleId,
                u.EnforceUnique,
                u.RequiresRemediation,
                u.Rationales.ToArray(),
                u.PreRemediationSql.ToArray(),
                u.Evidence.Select(ToEvidence).ToArray())).ToArray(),
            report.ForeignKeys.Select(static f => new PolicyDecisionLogForeignKey(
                f.Column.Schema.Value,
                f.Column.Table.Value,
                f.Column.Column.Value,
                f.RuleId,
                f.CreateConstraint,
                f.Rationales.ToArray(),
                f.PreRemediationSql.ToArray(),
                f.Evidence.Select(ToEvidence).ToArray())).ToArray(),
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
                    c.PhysicalName)).ToArray())).ToArray());

        var path = Path.Combine(outputDirectory, "policy-decisions.json");
        Directory.CreateDirectory(outputDirectory);
        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static PolicyDecisionLogEvidence ToEvidence(PolicyEvidenceLink evidence)
        => new(
            evidence.Source,
            evidence.Reference,
            evidence.IsPresent,
            evidence.Metrics);

    private sealed record PolicyDecisionLog(
        string SchemaVersion,
        DateTime GeneratedAtUtc,
        PolicyDecisionLogSummary Summary,
        IReadOnlyList<PolicyDecisionLogWarning> Warnings,
        IReadOnlyList<PolicyDecisionLogColumn> Columns,
        IReadOnlyList<PolicyDecisionLogUniqueIndex> UniqueIndexes,
        IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys,
        IReadOnlyList<PolicyDecisionLogDiagnostic> Diagnostics);

    private sealed record PolicyDecisionLogSummary(
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
        IReadOnlyDictionary<string, int> ForeignKeyRationales);

    private sealed record PolicyDecisionLogWarning(
        string Code,
        string Message,
        IReadOnlyList<PolicyDecisionLogEvidence> Evidence);

    private sealed record PolicyDecisionLogColumn(
        string Schema,
        string Table,
        string Column,
        string RuleId,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales,
        IReadOnlyList<string> PreRemediationSql,
        IReadOnlyList<PolicyDecisionLogEvidence> Evidence);

    private sealed record PolicyDecisionLogUniqueIndex(
        string Schema,
        string Table,
        string Index,
        string RuleId,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales,
        IReadOnlyList<string> PreRemediationSql,
        IReadOnlyList<PolicyDecisionLogEvidence> Evidence);

    private sealed record PolicyDecisionLogForeignKey(
        string Schema,
        string Table,
        string Column,
        string RuleId,
        bool CreateConstraint,
        IReadOnlyList<string> Rationales,
        IReadOnlyList<string> PreRemediationSql,
        IReadOnlyList<PolicyDecisionLogEvidence> Evidence);

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

    private sealed record PolicyDecisionLogEvidence(
        string Source,
        string Reference,
        bool IsPresent,
        IReadOnlyDictionary<string, string> Metrics);

    private sealed record PolicyDecisionLogDuplicateCandidate(string Module, string Schema, string PhysicalName);
}
