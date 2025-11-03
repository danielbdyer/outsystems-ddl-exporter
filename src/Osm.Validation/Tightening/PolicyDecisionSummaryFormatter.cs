using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Osm.Validation.Tightening;

public static class PolicyDecisionSummaryFormatter
{
    public static ImmutableArray<string> FormatForConsole(PolicyDecisionReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var entries = new List<SummaryEntry>();

        var bucketedColumns = report.Columns
            .Select(column => (column, bucket: Classify(column)))
            .Where(tuple => tuple.bucket != ColumnSummaryBucket.None)
            .GroupBy(tuple => tuple.bucket)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ColumnDecisionReport>)group.Select(tuple => tuple.column).ToList());

        // PROMINENT CONTRADICTION ALERT - Always at the top if contradictions exist
        AddIfPresent(entries, BuildContradictionAlert(report.Columns));

        // Informational summaries about what was tightened
        AddIfPresent(entries, BuildMandatorySummary(GetColumns(bucketedColumns, ColumnSummaryBucket.Mandatory)));
        AddIfPresent(entries, BuildForeignKeySummary(GetColumns(bucketedColumns, ColumnSummaryBucket.ForeignKey)));
        AddIfPresent(entries, BuildPrimaryKeySummary(GetColumns(bucketedColumns, ColumnSummaryBucket.PrimaryKey)));
        AddIfPresent(entries, BuildUniqueSummary(GetColumns(bucketedColumns, ColumnSummaryBucket.Unique)));
        AddIfPresent(entries, BuildPhysicalSummary(GetColumns(bucketedColumns, ColumnSummaryBucket.Physical)));

        // Unique indexes and FK creation
        AddIfPresent(entries, BuildUniqueIndexSummary(report.UniqueIndexes));
        AddIfPresent(entries, BuildForeignKeyCreationSummary(report.ForeignKeys));

        // Issues that blocked tightening
        AddIfPresent(entries, BuildRemediationSummary(GetColumns(bucketedColumns, ColumnSummaryBucket.Remediation)));
        AddIfPresent(entries, BuildProfileMissingSummary(report.Columns));

        if (entries.Count == 0)
        {
            entries.Add(new SummaryEntry(0, 999, "No column tightening actions were taken based on the current profile evidence."));
        }

        return entries
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Priority)
            .Select(entry => entry.Message)
            .ToImmutableArray();
    }

    private static ColumnSummaryBucket Classify(ColumnDecisionReport column)
    {
        if (!column.MakeNotNull)
        {
            return ColumnSummaryBucket.None;
        }

        if (column.RequiresRemediation)
        {
            return ColumnSummaryBucket.Remediation;
        }

        if (HasRationale(column, TighteningRationales.PrimaryKey))
        {
            return ColumnSummaryBucket.PrimaryKey;
        }

        if (HasRationale(column, TighteningRationales.ForeignKeyEnforced))
        {
            return ColumnSummaryBucket.ForeignKey;
        }

        if (HasRationale(column, TighteningRationales.Mandatory))
        {
            return ColumnSummaryBucket.Mandatory;
        }

        if (HasUniqueSignal(column))
        {
            return ColumnSummaryBucket.Unique;
        }

        if (HasRationale(column, TighteningRationales.PhysicalNotNull))
        {
            return ColumnSummaryBucket.Physical;
        }

        return ColumnSummaryBucket.None;
    }

    private static IReadOnlyList<ColumnDecisionReport> GetColumns(
        IReadOnlyDictionary<ColumnSummaryBucket, IReadOnlyList<ColumnDecisionReport>> source,
        ColumnSummaryBucket bucket)
        => source.TryGetValue(bucket, out var values) ? values : Array.Empty<ColumnDecisionReport>();

    private static SummaryEntry? BuildMandatorySummary(IReadOnlyList<ColumnDecisionReport> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var entityCount = CountEntities(columns);
        var dataAllClean = columns.All(column => HasRationale(column, TighteningRationales.DataNoNulls));
        var defaults = columns.Count(column => HasRationale(column, TighteningRationales.DefaultPresent));

        var builder = new StringBuilder();
        builder.Append(
            $"{FormatAttributeCount(columns.Count)} across {FormatEntityCount(entityCount)} were tightened to NOT NULL based on logical mandatory metadata");
        if (dataAllClean)
        {
            builder.Append(" after confirming the profiler reported no null rows");
        }

        builder.Append('.');
        if (defaults > 0)
        {
            builder.Append(' ');
            builder.Append(
                $"{defaults} {(defaults == 1 ? "attribute" : "attributes")} already ship with default values that will continue to be applied during tightening.");
        }

        return new SummaryEntry(columns.Count, 10, builder.ToString());
    }

    private static SummaryEntry? BuildForeignKeySummary(IReadOnlyList<ColumnDecisionReport> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var entityCount = CountEntities(columns);
        var builder = new StringBuilder();
        builder.Append(
            $"{FormatAttributeCount(columns.Count)} across {FormatEntityCount(entityCount)} were tightened to NOT NULL because referential integrity evidence supports enforcement");

        if (columns.All(column => HasRationale(column, TighteningRationales.DataNoNulls)))
        {
            builder.Append(" and the profiler found no null rows");
        }

        builder.Append('.');
        return new SummaryEntry(columns.Count, 20, builder.ToString());
    }

    private static SummaryEntry? BuildPrimaryKeySummary(IReadOnlyList<ColumnDecisionReport> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var entityCount = CountEntities(columns);
        var physical = columns.Count(column => HasRationale(column, TighteningRationales.PhysicalNotNull));
        var builder = new StringBuilder();
        builder.Append(
            $"{FormatAttributeCount(columns.Count)} primary key {(columns.Count == 1 ? "attribute" : "attributes")} across {FormatEntityCount(entityCount)} remained enforced as NOT NULL");

        if (physical > 0)
        {
            builder.Append(
                $" ({physical} {(physical == 1 ? "was" : "were")} already constrained physically)");
        }

        builder.Append('.');
        return new SummaryEntry(columns.Count, 30, builder.ToString());
    }

    private static SummaryEntry? BuildUniqueSummary(IReadOnlyList<ColumnDecisionReport> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var entityCount = CountEntities(columns);
        var builder = new StringBuilder();
        builder.Append(
            $"{FormatAttributeCount(columns.Count)} across {FormatEntityCount(entityCount)} gained NOT NULL enforcement based on clean unique index evidence");

        if (columns.All(column => HasRationale(column, TighteningRationales.DataNoNulls)))
        {
            builder.Append(" with zero nulls observed during profiling");
        }

        builder.Append('.');
        return new SummaryEntry(columns.Count, 40, builder.ToString());
    }

    private static SummaryEntry? BuildPhysicalSummary(IReadOnlyList<ColumnDecisionReport> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var entityCount = CountEntities(columns);
        var message =
            $"{FormatAttributeCount(columns.Count)} across {FormatEntityCount(entityCount)} inherited NOT NULL status from the physical database schema.";
        return new SummaryEntry(columns.Count, 50, message);
    }

    private static SummaryEntry? BuildRemediationSummary(IReadOnlyList<ColumnDecisionReport> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        var entityCount = CountEntities(columns);
        var message =
            $"{FormatAttributeCount(columns.Count)} across {FormatEntityCount(entityCount)} require remediation before tightening can be applied. Review the policy decision log for supporting evidence.";
        return new SummaryEntry(columns.Count, 60, message);
    }

    private static SummaryEntry? BuildUniqueIndexSummary(ImmutableArray<UniqueIndexDecisionReport> indexes)
    {
        var enforced = indexes.Where(index => index.EnforceUnique).ToArray();
        if (enforced.Length == 0)
        {
            return null;
        }

        var message =
            $"{FormatCount(enforced.Length, "unique index", "unique indexes")} were scripted as UNIQUE after validating the profiling evidence.";
        return new SummaryEntry(enforced.Length, 70, message);
    }

    private static SummaryEntry? BuildForeignKeyCreationSummary(ImmutableArray<ForeignKeyDecisionReport> foreignKeys)
    {
        var created = foreignKeys.Where(fk => fk.CreateConstraint).ToArray();
        if (created.Length == 0)
        {
            return null;
        }

        var message =
            $"{FormatCount(created.Length, "foreign key constraint", "foreign key constraints")} were scripted because database or policy conditions allowed enforcement.";
        return new SummaryEntry(created.Length, 80, message);
    }

    private static SummaryEntry? BuildContradictionAlert(ImmutableArray<ColumnDecisionReport> columns)
    {
        var nullContradictions = columns
            .Where(column => !column.MakeNotNull && HasRationale(column, TighteningRationales.DataHasNulls))
            .ToArray();

        var orphanContradictions = columns
            .Where(column => !column.MakeNotNull && HasRationale(column, TighteningRationales.DataHasOrphans))
            .ToArray();

        var totalContradictions = nullContradictions.Length + orphanContradictions.Length;

        if (totalContradictions == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("⚠️  ATTENTION: ");

        var parts = new List<string>();
        if (nullContradictions.Length > 0)
        {
            var entityCount = CountEntities(nullContradictions);
            parts.Add($"{FormatAttributeCount(nullContradictions.Length)} across {FormatEntityCount(entityCount)} have NULL values that contradict mandatory constraints");
        }

        if (orphanContradictions.Length > 0)
        {
            var entityCount = CountEntities(orphanContradictions);
            parts.Add($"{FormatAttributeCount(orphanContradictions.Length)} across {FormatEntityCount(entityCount)} have orphaned rows that violate referential integrity");
        }

        builder.Append(string.Join(" and ", parts));
        builder.Append(". Manual data remediation is required before tightening can proceed. Review the opportunities report for details.");

        // Priority 1 ensures this appears first
        return new SummaryEntry(totalContradictions, 1, builder.ToString());
    }

    private static SummaryEntry? BuildProfileMissingSummary(ImmutableArray<ColumnDecisionReport> columns)
    {
        var missing = columns
            .Where(column => !column.MakeNotNull && HasRationale(column, TighteningRationales.ProfileMissing))
            .ToArray();

        if (missing.Length == 0)
        {
            return null;
        }

        var entityCount = CountEntities(missing);
        var message =
            $"{FormatAttributeCount(missing.Length)} across {FormatEntityCount(entityCount)} stayed nullable because profiling evidence was unavailable.";
        return new SummaryEntry(missing.Length, 90, message);
    }

    private static SummaryEntry? BuildNullContradictionSummary(ImmutableArray<ColumnDecisionReport> columns)
    {
        var contradicting = columns
            .Where(column => !column.MakeNotNull && HasRationale(column, TighteningRationales.DataHasNulls))
            .ToArray();

        if (contradicting.Length == 0)
        {
            return null;
        }

        var entityCount = CountEntities(contradicting);
        var message =
            $"{FormatAttributeCount(contradicting.Length)} across {FormatEntityCount(entityCount)} stayed nullable because profiling detected NULL values.";
        return new SummaryEntry(contradicting.Length, 95, message);
    }

    private static SummaryEntry? BuildOrphanSummary(ImmutableArray<ColumnDecisionReport> columns)
    {
        var orphaned = columns
            .Where(column => !column.MakeNotNull && HasRationale(column, TighteningRationales.DataHasOrphans))
            .ToArray();

        if (orphaned.Length == 0)
        {
            return null;
        }

        var entityCount = CountEntities(orphaned);
        var ignoreRules = orphaned.Count(column => HasRationale(column, TighteningRationales.DeleteRuleIgnore));

        var builder = new StringBuilder();
        builder.Append(
            $"{FormatAttributeCount(orphaned.Length)} across {FormatEntityCount(entityCount)} stayed nullable because orphaned rows were detected");

        if (ignoreRules == orphaned.Length)
        {
            builder.Append(" under Ignore delete rules");
        }
        else if (ignoreRules > 0)
        {
            builder.Append($" and {ignoreRules} {(ignoreRules == 1 ? "attribute" : "attributes")} use Ignore delete rules");
        }

        builder.Append('.');
        return new SummaryEntry(orphaned.Length, 100, builder.ToString());
    }

    private static int CountEntities(IReadOnlyList<ColumnDecisionReport> columns)
    {
        return columns
            .Select(column => (Schema: column.Column.Schema.Value, Table: column.Column.Table.Value))
            .Distinct()
            .Count();
    }

    private static bool HasRationale(ColumnDecisionReport column, string rationale)
        => column.Rationales.Any(value => string.Equals(value, rationale, StringComparison.Ordinal));

    private static bool HasUniqueSignal(ColumnDecisionReport column)
        => column.Rationales.Any(value => string.Equals(value, TighteningRationales.UniqueNoNulls, StringComparison.Ordinal)
            || string.Equals(value, TighteningRationales.CompositeUniqueNoNulls, StringComparison.Ordinal));

    private static string FormatAttributeCount(int count)
        => FormatCount(count, "attribute", "attributes");

    private static string FormatEntityCount(int count)
        => FormatCount(count, "entity", "entities");

    private static string FormatCount(int count, string singular, string plural)
        => count == 1 ? $"1 {singular}" : $"{count} {plural}";

    private static void AddIfPresent(List<SummaryEntry> entries, SummaryEntry? candidate)
    {
        if (candidate.HasValue)
        {
            entries.Add(candidate.Value);
        }
    }

    private enum ColumnSummaryBucket
    {
        None = 0,
        Remediation = 1,
        PrimaryKey = 2,
        ForeignKey = 3,
        Mandatory = 4,
        Unique = 5,
        Physical = 6
    }

    private readonly record struct SummaryEntry(int Count, int Priority, string Message);
}

