using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class PolicyDecisionLogWriter
{
    public async Task<string> WriteAsync(string outputDirectory, PolicyDecisionReport report, CancellationToken cancellationToken = default)
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
        var togglePrecedence = report.Toggles.ToExportDictionary();

        var predicates = BuildPredicateTelemetry(report.Predicates);

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
                c.Column.Schema.Value,
                c.Column.Table.Value,
                c.Column.Column.Value,
                c.MakeNotNull,
                c.RequiresRemediation,
                c.Rationales.ToArray())).ToArray(),
            report.UniqueIndexes.Select(static u => new PolicyDecisionLogUniqueIndex(
                u.Index.Schema.Value,
                u.Index.Table.Value,
                u.Index.Index.Value,
                u.EnforceUnique,
                u.RequiresRemediation,
                u.Rationales.ToArray())).ToArray(),
            report.ForeignKeys.Select(static f => new PolicyDecisionLogForeignKey(
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
            predicates);

        var path = Path.Combine(outputDirectory, "policy-decisions.json");
        Directory.CreateDirectory(outputDirectory);
        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return path;
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
        PolicyDecisionLogPredicates Predicates);

    private sealed record PolicyDecisionLogColumn(
        string Schema,
        string Table,
        string Column,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogUniqueIndex(
        string Schema,
        string Table,
        string Index,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogForeignKey(
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

    private static PolicyDecisionLogPredicates BuildPredicateTelemetry(PredicateTelemetry predicates)
    {
        static IReadOnlyList<string> ToList(ImmutableArray<string> values)
            => values.IsDefaultOrEmpty ? Array.Empty<string>() : values.ToArray();

        var tables = predicates.Tables
            .Select(static table => new PolicyDecisionLogTablePredicate(
                table.Module,
                table.LogicalName,
                table.Schema,
                table.PhysicalName,
                ToList(table.Predicates)))
            .ToArray();

        var columns = predicates.Columns
            .Select(static column => new PolicyDecisionLogColumnPredicate(
                column.Module,
                column.Entity,
                column.Schema,
                column.Table,
                column.Column,
                ToList(column.Predicates)))
            .ToArray();

        var indexes = predicates.Indexes
            .Select(static index => new PolicyDecisionLogIndexPredicate(
                index.Module,
                index.Entity,
                index.Schema,
                index.Table,
                index.Index,
                ToList(index.Predicates)))
            .ToArray();

        var sequences = predicates.Sequences
            .Select(static sequence => new PolicyDecisionLogSequencePredicate(
                sequence.Schema,
                sequence.Name,
                ToList(sequence.Predicates)))
            .ToArray();

        var extended = predicates.ExtendedProperties
            .Select(static property => new PolicyDecisionLogExtendedPredicate(
                property.Scope,
                property.Module,
                property.Schema,
                property.Table,
                property.Column,
                ToList(property.Predicates)))
            .ToArray();

        return new PolicyDecisionLogPredicates(tables, columns, indexes, sequences, extended);
    }

    private sealed record PolicyDecisionLogPredicates(
        IReadOnlyList<PolicyDecisionLogTablePredicate> Tables,
        IReadOnlyList<PolicyDecisionLogColumnPredicate> Columns,
        IReadOnlyList<PolicyDecisionLogIndexPredicate> Indexes,
        IReadOnlyList<PolicyDecisionLogSequencePredicate> Sequences,
        IReadOnlyList<PolicyDecisionLogExtendedPredicate> ExtendedProperties);

    private sealed record PolicyDecisionLogTablePredicate(
        string Module,
        string LogicalName,
        string Schema,
        string Table,
        IReadOnlyList<string> Predicates);

    private sealed record PolicyDecisionLogColumnPredicate(
        string Module,
        string Entity,
        string Schema,
        string Table,
        string Column,
        IReadOnlyList<string> Predicates);

    private sealed record PolicyDecisionLogIndexPredicate(
        string Module,
        string Entity,
        string Schema,
        string Table,
        string Index,
        IReadOnlyList<string> Predicates);

    private sealed record PolicyDecisionLogSequencePredicate(
        string Schema,
        string Name,
        IReadOnlyList<string> Predicates);

    private sealed record PolicyDecisionLogExtendedPredicate(
        string Scope,
        string? Module,
        string? Schema,
        string? Table,
        string? Column,
        IReadOnlyList<string> Predicates);
}
