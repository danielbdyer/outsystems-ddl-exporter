using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public sealed record MultiEnvironmentProfileReport(
    ImmutableArray<ProfilingEnvironmentSummary> Environments,
    ImmutableArray<MultiEnvironmentFinding> Findings)
{
    public static MultiEnvironmentProfileReport Empty { get; } = new(
        ImmutableArray<ProfilingEnvironmentSummary>.Empty,
        ImmutableArray<MultiEnvironmentFinding>.Empty);

    public static MultiEnvironmentProfileReport Create(
        IEnumerable<ProfilingEnvironmentSnapshot> captures)
    {
        if (captures is null)
        {
            throw new ArgumentNullException(nameof(captures));
        }

        var snapshots = captures
            .Where(static capture => capture is not null)
            .ToImmutableArray();

        if (snapshots.IsDefaultOrEmpty || snapshots.Length == 0)
        {
            return Empty;
        }

        var summaries = snapshots
            .Select(static snapshot => ProfilingEnvironmentSummary.Create(snapshot))
            .ToImmutableArray();

        var findings = BuildFindings(summaries, snapshots);
        return new MultiEnvironmentProfileReport(summaries, findings);
    }

    private static ImmutableArray<MultiEnvironmentFinding> BuildFindings(
        ImmutableArray<ProfilingEnvironmentSummary> summaries,
        ImmutableArray<ProfilingEnvironmentSnapshot> snapshots)
    {
        if (summaries.IsDefaultOrEmpty || summaries.Length < 2)
        {
            return ImmutableArray<MultiEnvironmentFinding>.Empty;
        }

        var primary = summaries.FirstOrDefault(static summary => summary.IsPrimary)
            ?? summaries[0];

        var primarySnapshot = snapshots.FirstOrDefault(static snapshot => snapshot.IsPrimary)
            ?? snapshots[0];

        var primaryColumnNulls = BuildColumnNullLookup(primarySnapshot.Snapshot);
        var primaryUniqueViolations = BuildUniqueViolationLookup(primarySnapshot.Snapshot);
        var primaryCompositeViolations = BuildCompositeViolationLookup(primarySnapshot.Snapshot);
        var primaryForeignKeyOrphans = BuildForeignKeyLookup(primarySnapshot.Snapshot, static fk => fk.HasOrphan);
        var primaryForeignKeyProbeUnknown = BuildForeignKeyLookup(primarySnapshot.Snapshot, static fk => fk.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded);

        var builder = ImmutableArray.CreateBuilder<MultiEnvironmentFinding>();

        foreach (var summary in summaries)
        {
            if (summary.IsPrimary)
            {
                continue;
            }

            var snapshot = snapshots.FirstOrDefault(s => string.Equals(s.Name, summary.Name, StringComparison.Ordinal));
            var affectedObjects = ImmutableArray<string>.Empty;

            if (summary.ColumnsWithNulls > primary.ColumnsWithNulls)
            {
                affectedObjects = snapshot is null
                    ? ImmutableArray<string>.Empty
                    : IdentifyNullDrift(snapshot.Snapshot, primaryColumnNulls);

                builder.Add(new MultiEnvironmentFinding(
                    code: "profiling.multiEnvironment.nulls",
                    title: $"{summary.Name}: elevated null counts",
                    severity: MultiEnvironmentFindingSeverity.Warning,
                    summary: string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} columns reported null values compared to {1} in {2}.",
                        summary.ColumnsWithNulls,
                        primary.ColumnsWithNulls,
                        primary.Name),
                    suggestedAction: "Investigate data quality issues in this environment before enforcing NOT NULL policies.",
                    affectedObjects: affectedObjects));
            }

            if (summary.UniqueViolations > primary.UniqueViolations)
            {
                affectedObjects = snapshot is null
                    ? ImmutableArray<string>.Empty
                    : IdentifyUniqueDrift(snapshot.Snapshot, primaryUniqueViolations, primaryCompositeViolations);

                builder.Add(new MultiEnvironmentFinding(
                    code: "profiling.multiEnvironment.uniqueness",
                    title: $"{summary.Name}: uniqueness drift",
                    severity: MultiEnvironmentFindingSeverity.Warning,
                    summary: string.Format(
                        CultureInfo.InvariantCulture,
                        "Detected {0} unique constraint violation(s) versus {1} in {2}.",
                        summary.UniqueViolations,
                        primary.UniqueViolations,
                        primary.Name),
                    suggestedAction: "Review candidate keys in this environment and remediate duplicates before promotion.",
                    affectedObjects: affectedObjects));
            }

            if (summary.ForeignKeyOrphans > primary.ForeignKeyOrphans)
            {
                affectedObjects = snapshot is null
                    ? ImmutableArray<string>.Empty
                    : IdentifyForeignKeyIssues(snapshot.Snapshot, primaryForeignKeyOrphans, static fk => fk.HasOrphan, "FK");

                builder.Add(new MultiEnvironmentFinding(
                    code: "profiling.multiEnvironment.foreignKey",
                    title: $"{summary.Name}: orphaned foreign keys",
                    severity: MultiEnvironmentFindingSeverity.Critical,
                    summary: string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} orphaned foreign key reference(s) detected while {1} reports {2}.",
                        summary.ForeignKeyOrphans,
                        primary.Name,
                        primary.ForeignKeyOrphans),
                    suggestedAction: "Repair orphaned relationships or adjust policy exclusions before enforcing foreign keys.",
                    affectedObjects: affectedObjects));
            }

            if (summary.ForeignKeyProbeUnknown > 0)
            {
                affectedObjects = snapshot is null
                    ? ImmutableArray<string>.Empty
                    : IdentifyForeignKeyIssues(snapshot.Snapshot, primaryForeignKeyProbeUnknown, static fk => fk.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded, "Probe");

                builder.Add(new MultiEnvironmentFinding(
                    code: "profiling.multiEnvironment.foreignKey.evidence",
                    title: $"{summary.Name}: foreign key evidence gaps",
                    severity: MultiEnvironmentFindingSeverity.Advisory,
                    summary: string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} foreign key probe(s) did not complete in this environment.",
                        summary.ForeignKeyProbeUnknown),
                    suggestedAction: "Extend sampling thresholds or re-run profiling with increased timeouts for this environment.",
                    affectedObjects: affectedObjects));
            }

            if (summary.Duration > primary.Duration + TimeSpan.FromMinutes(1))
            {
                builder.Add(new MultiEnvironmentFinding(
                    code: "profiling.multiEnvironment.duration",
                    title: $"{summary.Name}: slower profiling",
                    severity: MultiEnvironmentFindingSeverity.Advisory,
                    summary: string.Format(
                        CultureInfo.InvariantCulture,
                        "Profiling took {0:g} versus {1:g} in {2}.",
                        summary.Duration,
                        primary.Duration,
                        primary.Name),
                    suggestedAction: "Review connection locality or apply sampling overrides to align runtime with other environments.",
                    affectedObjects: ImmutableArray<string>.Empty));
            }
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<string> IdentifyNullDrift(
        ProfileSnapshot snapshot,
        IReadOnlyDictionary<(string Schema, string Table, string Column), long> primaryNulls)
    {
        var impactedColumns = snapshot.Columns
            .Where(column => column.NullCount > 0)
            .Select(column =>
            {
                var key = (column.Schema.Value, column.Table.Value, column.Column.Value);
                primaryNulls.TryGetValue(key, out var primaryNullCount);
                var delta = column.NullCount - primaryNullCount;
                return new
                {
                    Column = column,
                    Delta = delta,
                    PrimaryNulls = primaryNullCount
                };
            })
            .Where(item => item.Delta > 0)
            .OrderByDescending(item => item.Delta)
            .ThenBy(item => item.Column.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Column.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Column.Column.Value, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(item => string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2} (+{3:N0} NULLs; primary {4:N0})",
                item.Column.Schema.Value,
                item.Column.Table.Value,
                item.Column.Column.Value,
                item.Delta,
                item.PrimaryNulls))
            .ToImmutableArray();

        return impactedColumns;
    }

    private static ImmutableArray<string> IdentifyUniqueDrift(
        ProfileSnapshot snapshot,
        ISet<(string Schema, string Table, string Column)> primarySingles,
        ISet<(string Schema, string Table, string Columns)> primaryComposites)
    {
        var offenders = new List<string>();

        foreach (var candidate in snapshot.UniqueCandidates.Where(static candidate => candidate.HasDuplicate))
        {
            var key = (candidate.Schema.Value, candidate.Table.Value, candidate.Column.Value);
            if (primarySingles.Contains(key))
            {
                continue;
            }

            offenders.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}",
                candidate.Schema.Value,
                candidate.Table.Value,
                candidate.Column.Value));
        }

        foreach (var composite in snapshot.CompositeUniqueCandidates.Where(static candidate => candidate.HasDuplicate))
        {
            var key = (
                composite.Schema.Value,
                composite.Table.Value,
                ProfilingPlanBuilder.BuildUniqueKey(composite.Columns.Select(static column => column.Value)));

            if (primaryComposites.Contains(key))
            {
                continue;
            }

            var descriptor = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1} ({2})",
                composite.Schema.Value,
                composite.Table.Value,
                string.Join(
                    ", ",
                    composite.Columns.Select(static column => column.Value)));
            offenders.Add(descriptor);
        }

        return offenders
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToImmutableArray();
    }

    private static ImmutableArray<string> IdentifyForeignKeyIssues(
        ProfileSnapshot snapshot,
        ISet<string> primaryReferences,
        Func<ForeignKeyReality, bool> predicate,
        string issueKind)
    {
        var offenders = snapshot.ForeignKeys
            .Where(predicate)
            .Select(fk => (Reference: fk.Reference, Key: BuildForeignKeyKey(fk.Reference)))
            .Where(tuple => !primaryReferences.Contains(tuple.Key))
            .OrderBy(tuple => tuple.Reference.FromSchema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tuple => tuple.Reference.FromTable.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tuple => tuple.Reference.FromColumn.Value, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(tuple => string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2} → {3}.{4}.{5} ({6})",
                tuple.Reference.FromSchema.Value,
                tuple.Reference.FromTable.Value,
                tuple.Reference.FromColumn.Value,
                tuple.Reference.ToSchema.Value,
                tuple.Reference.ToTable.Value,
                tuple.Reference.ToColumn.Value,
                issueKind))
            .ToImmutableArray();

        return offenders;
    }

    private static IReadOnlyDictionary<(string Schema, string Table, string Column), long> BuildColumnNullLookup(ProfileSnapshot snapshot)
    {
        var lookup = new Dictionary<(string Schema, string Table, string Column), long>(EnvironmentColumnKeyComparer.Instance);

        foreach (var column in snapshot.Columns)
        {
            lookup[(column.Schema.Value, column.Table.Value, column.Column.Value)] = column.NullCount;
        }

        return lookup;
    }

    private static ISet<(string Schema, string Table, string Column)> BuildUniqueViolationLookup(ProfileSnapshot snapshot)
    {
        var lookup = new HashSet<(string Schema, string Table, string Column)>(EnvironmentColumnKeyComparer.Instance);

        foreach (var candidate in snapshot.UniqueCandidates.Where(static candidate => candidate.HasDuplicate))
        {
            lookup.Add((candidate.Schema.Value, candidate.Table.Value, candidate.Column.Value));
        }

        return lookup;
    }

    private static ISet<(string Schema, string Table, string Columns)> BuildCompositeViolationLookup(ProfileSnapshot snapshot)
    {
        var lookup = new HashSet<(string Schema, string Table, string Columns)>(EnvironmentCompositeUniqueKeyComparer.Instance);

        foreach (var candidate in snapshot.CompositeUniqueCandidates.Where(static candidate => candidate.HasDuplicate))
        {
            var key = (
                candidate.Schema.Value,
                candidate.Table.Value,
                ProfilingPlanBuilder.BuildUniqueKey(candidate.Columns.Select(static column => column.Value)));
            lookup.Add(key);
        }

        return lookup;
    }

    private static ISet<string> BuildForeignKeyLookup(
        ProfileSnapshot snapshot,
        Func<ForeignKeyReality, bool> predicate)
    {
        var lookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reality in snapshot.ForeignKeys.Where(predicate))
        {
            lookup.Add(BuildForeignKeyKey(reality.Reference));
        }

        return lookup;
    }

    private static string BuildForeignKeyKey(ForeignKeyReference reference)
    {
        return ProfilingPlanBuilder.BuildForeignKeyKey(
            reference.FromColumn.Value,
            reference.ToSchema.Value,
            reference.ToTable.Value,
            reference.ToColumn.Value);
    }
}

public sealed record ProfilingEnvironmentSnapshot(
    string Name,
    bool IsPrimary,
    MultiTargetSqlDataProfiler.EnvironmentLabelOrigin LabelOrigin,
    bool LabelWasAdjusted,
    ProfileSnapshot Snapshot,
    TimeSpan Duration);

public sealed record ProfilingEnvironmentSummary(
    string Name,
    bool IsPrimary,
    MultiTargetSqlDataProfiler.EnvironmentLabelOrigin LabelOrigin,
    bool LabelWasAdjusted,
    int ColumnCount,
    int ColumnsWithNulls,
    int ColumnsWithUnknownNullStatus,
    int UniqueCandidateCount,
    int UniqueViolations,
    int UniqueProbeUnknown,
    int CompositeUniqueCount,
    int CompositeUniqueViolations,
    int ForeignKeyCount,
    int ForeignKeyOrphans,
    int ForeignKeyProbeUnknown,
    int ForeignKeyNoCheck,
    TimeSpan Duration)
{
    public static ProfilingEnvironmentSummary Create(ProfilingEnvironmentSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var profile = snapshot.Snapshot ?? throw new ArgumentException("Snapshot must be provided.", nameof(snapshot));

        var columns = profile.Columns;
        var uniqueCandidates = profile.UniqueCandidates;
        var compositeCandidates = profile.CompositeUniqueCandidates;
        var foreignKeys = profile.ForeignKeys;

        var columnsWithNulls = columns.Count(static column => column.NullCount > 0);
        var columnsWithUnknownNullStatus = columns.Count(static column => column.NullCountStatus.Outcome != ProfilingProbeOutcome.Succeeded);
        var uniqueViolations = uniqueCandidates.Count(static candidate => candidate.HasDuplicate)
            + compositeCandidates.Count(static candidate => candidate.HasDuplicate);
        var uniqueProbeUnknown = uniqueCandidates.Count(static candidate => candidate.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded);
        var foreignKeyOrphans = foreignKeys.Count(static fk => fk.HasOrphan);
        var foreignKeyProbeUnknown = foreignKeys.Count(static fk => fk.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded);
        var foreignKeyNoCheck = foreignKeys.Count(static fk => fk.IsNoCheck);

        return new ProfilingEnvironmentSummary(
            snapshot.Name,
            snapshot.IsPrimary,
            snapshot.LabelOrigin,
            snapshot.LabelWasAdjusted,
            columns.Length,
            columnsWithNulls,
            columnsWithUnknownNullStatus,
            uniqueCandidates.Length,
            uniqueViolations,
            uniqueProbeUnknown,
            compositeCandidates.Length,
            compositeCandidates.Count(static candidate => candidate.HasDuplicate),
            foreignKeys.Length,
            foreignKeyOrphans,
            foreignKeyProbeUnknown,
            foreignKeyNoCheck,
            snapshot.Duration);
    }
}

public enum MultiEnvironmentFindingSeverity
{
    Info,
    Advisory,
    Warning,
    Critical
}

public sealed class MultiEnvironmentFinding
{
    public MultiEnvironmentFinding(
        string code,
        string title,
        string summary,
        MultiEnvironmentFindingSeverity severity,
        string suggestedAction,
        ImmutableArray<string> affectedObjects)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Finding code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Finding title must be provided.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Finding summary must be provided.", nameof(summary));
        }

        Code = code;
        Title = title;
        Summary = summary;
        Severity = severity;
        SuggestedAction = string.IsNullOrWhiteSpace(suggestedAction)
            ? "No action required."
            : suggestedAction;
        AffectedObjects = affectedObjects.IsDefault
            ? ImmutableArray<string>.Empty
            : affectedObjects;
    }

    public string Code { get; }

    public string Title { get; }

    public string Summary { get; }

    public MultiEnvironmentFindingSeverity Severity { get; }

    public string SuggestedAction { get; }

    public ImmutableArray<string> AffectedObjects { get; }
}

internal sealed class EnvironmentColumnKeyComparer : IEqualityComparer<(string Schema, string Table, string Column)>
{
    public static EnvironmentColumnKeyComparer Instance { get; } = new();

    public bool Equals((string Schema, string Table, string Column) x, (string Schema, string Table, string Column) y)
    {
        return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string Schema, string Table, string Column) obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Column, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}

internal sealed class EnvironmentCompositeUniqueKeyComparer : IEqualityComparer<(string Schema, string Table, string Columns)>
{
    public static EnvironmentCompositeUniqueKeyComparer Instance { get; } = new();

    public bool Equals((string Schema, string Table, string Columns) x, (string Schema, string Table, string Columns) y)
    {
        return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Columns, y.Columns, StringComparison.Ordinal);
    }

    public int GetHashCode((string Schema, string Table, string Columns) obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Columns, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
