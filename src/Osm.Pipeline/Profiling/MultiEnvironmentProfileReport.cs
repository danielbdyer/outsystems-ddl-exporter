using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Profiling;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

public sealed record MultiEnvironmentProfileReport(
    ImmutableArray<ProfilingEnvironmentSummary> Environments,
    ImmutableArray<MultiEnvironmentFinding> Findings,
    MultiEnvironmentConstraintConsensus ConstraintConsensus)
{
    public static MultiEnvironmentProfileReport Empty { get; } = new(
        ImmutableArray<ProfilingEnvironmentSummary>.Empty,
        ImmutableArray<MultiEnvironmentFinding>.Empty,
        MultiEnvironmentConstraintConsensus.Empty);

    public static MultiEnvironmentProfileReport Create(
        IEnumerable<ProfilingEnvironmentSnapshot> captures,
        double minimumConsensusThreshold = 1.0)
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

        var normalizedSnapshots = ProfilingSnapshotNormalizer.Normalize(snapshots);

        var summaries = normalizedSnapshots
            .Select(static snapshot => ProfilingEnvironmentSummary.Create(snapshot))
            .ToImmutableArray();

        var findingsBuilder = BuildFindings(summaries, normalizedSnapshots).ToBuilder();

        var validationResult = ProfilingStandardizationValidator.Instance.ValidateMultiEnvironment(normalizedSnapshots);
        if (validationResult.IsSuccess)
        {
            var validationFindings = BuildValidationFindings(validationResult.Value);
            if (!validationFindings.IsDefaultOrEmpty && validationFindings.Length > 0)
            {
                findingsBuilder.AddRange(validationFindings);
            }
        }

        // Analyze constraint consensus across all environments
        var consensus = MultiEnvironmentConstraintConsensus.Analyze(normalizedSnapshots, minimumConsensusThreshold);

        return new MultiEnvironmentProfileReport(summaries, findingsBuilder.ToImmutable(), consensus);
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

        var primaryUniqueViolations = BuildUniqueViolationLookup(primarySnapshot.Snapshot);
        var primaryCompositeViolations = BuildCompositeViolationLookup(primarySnapshot.Snapshot);
        var primaryForeignKeyOrphans = BuildForeignKeyLookup(primarySnapshot.Snapshot, static fk => fk.HasOrphan);
        var primaryForeignKeyProbeUnknown = BuildForeignKeyLookup(primarySnapshot.Snapshot, static fk => fk.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded);
        var primaryNotNullViolations = BuildNotNullViolationLookup(primarySnapshot.Snapshot);

        var builder = ImmutableArray.CreateBuilder<MultiEnvironmentFinding>();

        foreach (var summary in summaries)
        {
            if (summary.IsPrimary)
            {
                continue;
            }

            var snapshot = snapshots.FirstOrDefault(s => string.Equals(s.Name, summary.Name, StringComparison.Ordinal));
            var affectedObjects = ImmutableArray<string>.Empty;

            var notNullVariance = snapshot is null
                ? (Count: 0, Offenders: ImmutableArray<string>.Empty)
                : IdentifyNotNullViolations(snapshot.Snapshot, primaryNotNullViolations);

            if (notNullVariance.Count > 0)
            {
                builder.Add(new MultiEnvironmentFinding(
                    code: "profiling.multiEnvironment.nullability",
                    title: $"{summary.Name}: NOT NULL violations",
                    severity: MultiEnvironmentFindingSeverity.Critical,
                    summary: string.Format(
                        CultureInfo.InvariantCulture,
                        "Detected {0} NOT NULL violation(s) not observed in {1}.",
                        notNullVariance.Count,
                        primary.Name),
                    suggestedAction: "Remediate null values or adjust policy exclusions before enforcing NOT NULL constraints.",
                    affectedObjects: notNullVariance.Offenders));
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

        return builder.ToImmutable();
    }

    private static ImmutableArray<MultiEnvironmentFinding> BuildValidationFindings(
        MultiEnvironmentValidationSummary summary)
    {
        if (summary is null || summary.AllIssues.IsDefaultOrEmpty || summary.AllIssues.Length == 0)
        {
            return ImmutableArray<MultiEnvironmentFinding>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<MultiEnvironmentFinding>();

        foreach (var issue in summary.AllIssues)
        {
            if (issue is null || issue.Severity == ValidationIssueSeverity.Info)
            {
                continue;
            }

            if (!IsCrossEnvironmentIssue(issue.Code))
            {
                continue;
            }

            var severity = MapSeverity(issue.Severity);
            var title = string.IsNullOrWhiteSpace(issue.Target)
                ? issue.Code
                : issue.Target;
            var affectedObjects = string.IsNullOrWhiteSpace(issue.Target)
                ? ImmutableArray<string>.Empty
                : ImmutableArray.Create(issue.Target);

            builder.Add(new MultiEnvironmentFinding(
                issue.Code,
                title,
                issue.Message,
                severity,
                ResolveSuggestedAction(issue.Code, severity),
                affectedObjects));
        }

        return builder.ToImmutable();
    }

    private static bool IsCrossEnvironmentIssue(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return code.Contains(".schema.", StringComparison.OrdinalIgnoreCase)
            || code.Contains(".dataQuality.", StringComparison.OrdinalIgnoreCase)
            || code.Contains(".constraint.", StringComparison.OrdinalIgnoreCase)
            || code.Contains(".case.", StringComparison.OrdinalIgnoreCase);
    }

    private static MultiEnvironmentFindingSeverity MapSeverity(ValidationIssueSeverity severity)
    {
        return severity switch
        {
            ValidationIssueSeverity.Error => MultiEnvironmentFindingSeverity.Critical,
            ValidationIssueSeverity.Warning => MultiEnvironmentFindingSeverity.Warning,
            ValidationIssueSeverity.Advisory => MultiEnvironmentFindingSeverity.Advisory,
            _ => MultiEnvironmentFindingSeverity.Advisory
        };
    }

    private static string ResolveSuggestedAction(string code, MultiEnvironmentFindingSeverity severity)
    {
        return code switch
        {
            "profiling.validation.schema.tableMissing" =>
                "Ensure the table exists in every profiled environment or provide a tableNameMappings entry to align names.",
            "profiling.validation.dataQuality.nullVariance" =>
                "Monitor null-handling differences across environments when planning NOT NULL enforcement.",
            "profiling.validation.constraint.uniqueDisagreement" =>
                "Remediate duplicates in outlier environments or defer UNIQUE constraint application until alignment is complete.",
            "profiling.validation.case.table.inconsistent" =>
                "Standardize table casing or add naming overrides so downstream tooling can match entities deterministically.",
            _ => severity switch
            {
                MultiEnvironmentFindingSeverity.Critical =>
                    "Resolve this discrepancy before promoting constraint hardening to avoid deployment failures.",
                MultiEnvironmentFindingSeverity.Warning =>
                    "Plan remediation for this variance so constraints behave consistently across environments.",
                _ => "Review this advisory when planning multi-environment readiness work."
            }
        };
    }

    private static (int Count, ImmutableArray<string> Offenders) IdentifyNotNullViolations(
        ProfileSnapshot snapshot,
        ISet<(string Schema, string Table, string Column)> primaryViolations)
    {
        var offenders = snapshot.Columns
            .Where(static column => !column.IsNullablePhysical && column.NullCount > 0)
            .Select(column => (
                Schema: column.Schema.Value,
                Table: column.Table.Value,
                Column: column.Column.Value))
            .Where(tuple => !primaryViolations.Contains(tuple))
            .ToList();

        var display = offenders
            .OrderBy(static tuple => tuple.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static tuple => tuple.Table, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static tuple => tuple.Column, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(tuple => string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}",
                tuple.Schema,
                tuple.Table,
                tuple.Column))
            .ToImmutableArray();

        return (offenders.Count, display);
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

    private static ISet<(string Schema, string Table, string Column)> BuildNotNullViolationLookup(ProfileSnapshot snapshot)
    {
        var lookup = new HashSet<(string Schema, string Table, string Column)>(EnvironmentColumnKeyComparer.Instance);

        foreach (var column in snapshot.Columns.Where(static column => !column.IsNullablePhysical && column.NullCount > 0))
        {
            lookup.Add((column.Schema.Value, column.Table.Value, column.Column.Value));
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
    TimeSpan Duration,
    ImmutableArray<TableNameMapping> TableNameMappings)
{
    public ProfilingEnvironmentSnapshot(
        string name,
        bool isPrimary,
        MultiTargetSqlDataProfiler.EnvironmentLabelOrigin labelOrigin,
        bool labelWasAdjusted,
        ProfileSnapshot snapshot,
        TimeSpan duration)
        : this(
            name,
            isPrimary,
            labelOrigin,
            labelWasAdjusted,
            snapshot,
            duration,
            ImmutableArray<TableNameMapping>.Empty)
    {
    }
}

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
            && string.Equals(x.Columns, y.Columns, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string Schema, string Table, string Columns) obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Columns, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}
