using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

/// <summary>
/// Analyzes constraint readiness across multiple environments to determine
/// which constraints can be safely applied to all surveyed databases.
/// </summary>
public sealed class MultiEnvironmentConstraintConsensus
{
    public ImmutableArray<ConstraintConsensusResult> NullabilityConsensus { get; }
    public ImmutableArray<ConstraintConsensusResult> UniqueConstraintConsensus { get; }
    public ImmutableArray<ConstraintConsensusResult> ForeignKeyConsensus { get; }
    public ConsensusStatistics Statistics { get; }

    private MultiEnvironmentConstraintConsensus(
        ImmutableArray<ConstraintConsensusResult> nullabilityConsensus,
        ImmutableArray<ConstraintConsensusResult> uniqueConstraintConsensus,
        ImmutableArray<ConstraintConsensusResult> foreignKeyConsensus,
        ConsensusStatistics statistics)
    {
        NullabilityConsensus = nullabilityConsensus;
        UniqueConstraintConsensus = uniqueConstraintConsensus;
        ForeignKeyConsensus = foreignKeyConsensus;
        Statistics = statistics;
    }

    /// <summary>
    /// Analyzes profiling snapshots from multiple environments to determine constraint consensus.
    /// </summary>
    /// <param name="snapshots">Profiling snapshots from all environments</param>
    /// <param name="minimumConsensusThreshold">Minimum percentage of environments that must agree (0.0-1.0)</param>
    public static MultiEnvironmentConstraintConsensus Analyze(
        IEnumerable<ProfilingEnvironmentSnapshot> snapshots,
        double minimumConsensusThreshold = 1.0)
    {
        if (snapshots is null)
        {
            throw new ArgumentNullException(nameof(snapshots));
        }

        if (minimumConsensusThreshold < 0.0 || minimumConsensusThreshold > 1.0)
        {
            throw new ArgumentException("Consensus threshold must be between 0.0 and 1.0", nameof(minimumConsensusThreshold));
        }

        var snapshotList = snapshots.Where(s => s is not null).ToImmutableArray();
        if (snapshotList.IsDefaultOrEmpty || snapshotList.Length == 0)
        {
            return Empty;
        }

        var environmentCount = snapshotList.Length;

        var nullabilityConsensus = AnalyzeNullabilityConsensus(snapshotList, environmentCount, minimumConsensusThreshold);
        var uniqueConsensus = AnalyzeUniqueConstraintConsensus(snapshotList, environmentCount, minimumConsensusThreshold);
        var foreignKeyConsensus = AnalyzeForeignKeyConsensus(snapshotList, environmentCount, minimumConsensusThreshold);

        var statistics = new ConsensusStatistics(
            environmentCount,
            nullabilityConsensus.Count(r => r.IsSafeToApply),
            nullabilityConsensus.Count(r => !r.IsSafeToApply),
            uniqueConsensus.Count(r => r.IsSafeToApply),
            uniqueConsensus.Count(r => !r.IsSafeToApply),
            foreignKeyConsensus.Count(r => r.IsSafeToApply),
            foreignKeyConsensus.Count(r => !r.IsSafeToApply));

        return new MultiEnvironmentConstraintConsensus(
            nullabilityConsensus.ToImmutableArray(),
            uniqueConsensus.ToImmutableArray(),
            foreignKeyConsensus.ToImmutableArray(),
            statistics);
    }

    public static MultiEnvironmentConstraintConsensus Empty { get; } = new(
        ImmutableArray<ConstraintConsensusResult>.Empty,
        ImmutableArray<ConstraintConsensusResult>.Empty,
        ImmutableArray<ConstraintConsensusResult>.Empty,
        new ConsensusStatistics(0, 0, 0, 0, 0, 0, 0));

    private static IEnumerable<ConstraintConsensusResult> AnalyzeNullabilityConsensus(
        ImmutableArray<ProfilingEnvironmentSnapshot> snapshots,
        int totalEnvironments,
        double threshold)
    {
        // Group columns by (schema, table, column) across all environments
        var columnsByKey = snapshots
            .SelectMany(env => env.Snapshot.Columns.Select(col => new { Environment = env.Name, Column = col }))
            .GroupBy(item => (item.Column.Schema.Value, item.Column.Table.Value, item.Column.Column.Value),
                ColumnKeyComparer.Instance);

        foreach (var group in columnsByKey)
        {
            var key = group.Key;
            var columns = group.ToList();
            var environmentsPresent = columns.Count;

            // Count environments where NOT NULL is safe (NullCount == 0 and successful probe)
            var safeEnvironments = columns.Count(item =>
                item.Column.NullCount == 0 &&
                item.Column.NullCountStatus.Outcome == ProfilingProbeOutcome.Succeeded);

            var consensusRatio = environmentsPresent > 0 ? (double)safeEnvironments / environmentsPresent : 0.0;
            var isSafe = consensusRatio >= threshold;

            var maxNullCount = columns.Max(item => item.Column.NullCount);
            var environmentsWithNulls = columns.Where(item => item.Column.NullCount > 0)
                .Select(item => item.Environment)
                .ToImmutableArray();

            yield return new ConstraintConsensusResult(
                ConstraintType.NotNull,
                $"{key.Schema}.{key.Table}.{key.Column}",
                isSafe,
                safeEnvironments,
                environmentsPresent,
                consensusRatio,
                isSafe
                    ? "NOT NULL constraint can be safely applied across all environments."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "NOT NULL constraint would fail. Maximum null count: {0:N0}. Environments with nulls: {1}",
                        maxNullCount,
                        string.Join(", ", environmentsWithNulls)));
        }
    }

    private static IEnumerable<ConstraintConsensusResult> AnalyzeUniqueConstraintConsensus(
        ImmutableArray<ProfilingEnvironmentSnapshot> snapshots,
        int totalEnvironments,
        double threshold)
    {
        // Analyze single-column unique constraints
        var uniqueByKey = snapshots
            .SelectMany(env => env.Snapshot.UniqueCandidates.Select(u => new { Environment = env.Name, Unique = u }))
            .GroupBy(item => (item.Unique.Schema.Value, item.Unique.Table.Value, item.Unique.Column.Value),
                ColumnKeyComparer.Instance);

        foreach (var group in uniqueByKey)
        {
            var key = group.Key;
            var uniques = group.ToList();
            var environmentsPresent = uniques.Count;

            var safeEnvironments = uniques.Count(item =>
                !item.Unique.HasDuplicate &&
                item.Unique.ProbeStatus.Outcome == ProfilingProbeOutcome.Succeeded);

            var consensusRatio = environmentsPresent > 0 ? (double)safeEnvironments / environmentsPresent : 0.0;
            var isSafe = consensusRatio >= threshold;

            var environmentsWithDuplicates = uniques.Where(item => item.Unique.HasDuplicate)
                .Select(item => item.Environment)
                .ToImmutableArray();

            yield return new ConstraintConsensusResult(
                ConstraintType.Unique,
                $"{key.Schema}.{key.Table}.{key.Column}",
                isSafe,
                safeEnvironments,
                environmentsPresent,
                consensusRatio,
                isSafe
                    ? "UNIQUE constraint can be safely applied across all environments."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "UNIQUE constraint would fail. Environments with duplicates: {0}",
                        string.Join(", ", environmentsWithDuplicates)));
        }

        // Analyze composite unique constraints
        var compositeByKey = snapshots
            .SelectMany(env => env.Snapshot.CompositeUniqueCandidates.Select(c => new { Environment = env.Name, Composite = c }))
            .GroupBy(item => (
                    item.Composite.Schema.Value,
                    item.Composite.Table.Value,
                    ProfilingPlanBuilder.BuildUniqueKey(item.Composite.Columns.Select(col => col.Value))),
                (tuple, items) => new { Key = tuple, Items = items.ToList() });

        foreach (var group in compositeByKey)
        {
            var key = group.Key;
            var composites = group.Items;
            var environmentsPresent = composites.Count;

            var safeEnvironments = composites.Count(item =>
                !item.Composite.HasDuplicate &&
                item.Composite.ProbeStatus.Outcome == ProfilingProbeOutcome.Succeeded);

            var consensusRatio = environmentsPresent > 0 ? (double)safeEnvironments / environmentsPresent : 0.0;
            var isSafe = consensusRatio >= threshold;

            var firstComposite = composites.First().Composite;
            var columnList = string.Join(", ", firstComposite.Columns.Select(c => c.Value));
            var descriptor = $"{key.Schema}.{key.Table} ({columnList})";

            var environmentsWithDuplicates = composites.Where(item => item.Composite.HasDuplicate)
                .Select(item => item.Environment)
                .ToImmutableArray();

            yield return new ConstraintConsensusResult(
                ConstraintType.CompositeUnique,
                descriptor,
                isSafe,
                safeEnvironments,
                environmentsPresent,
                consensusRatio,
                isSafe
                    ? "UNIQUE constraint can be safely applied across all environments."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "UNIQUE constraint would fail. Environments with duplicates: {0}",
                        string.Join(", ", environmentsWithDuplicates)));
        }
    }

    private static IEnumerable<ConstraintConsensusResult> AnalyzeForeignKeyConsensus(
        ImmutableArray<ProfilingEnvironmentSnapshot> snapshots,
        int totalEnvironments,
        double threshold)
    {
        var foreignKeysByKey = snapshots
            .SelectMany(env => env.Snapshot.ForeignKeys.Select(fk => new { Environment = env.Name, ForeignKey = fk }))
            .GroupBy(item =>
            {
                var fk = item.ForeignKey.Reference;
                return (fk.FromSchema.Value, fk.FromTable.Value, fk.FromColumn.Value,
                    fk.ToSchema.Value, fk.ToTable.Value, fk.ToColumn.Value);
            }, ForeignKeyKeyComparer.Instance);

        foreach (var group in foreignKeysByKey)
        {
            var key = group.Key;
            var foreignKeys = group.ToList();
            var environmentsPresent = foreignKeys.Count;

            var safeEnvironments = foreignKeys.Count(item =>
                !item.ForeignKey.HasOrphan &&
                !item.ForeignKey.IsNoCheck &&
                item.ForeignKey.ProbeStatus.Outcome == ProfilingProbeOutcome.Succeeded);

            var consensusRatio = environmentsPresent > 0 ? (double)safeEnvironments / environmentsPresent : 0.0;
            var isSafe = consensusRatio >= threshold;

            var environmentsWithOrphans = foreignKeys.Where(item => item.ForeignKey.HasOrphan)
                .Select(item => item.Environment)
                .ToImmutableArray();

            var descriptor = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2} -> {3}.{4}.{5}",
                key.FromSchema, key.FromTable, key.FromColumn,
                key.ToSchema, key.ToTable, key.ToColumn);

            yield return new ConstraintConsensusResult(
                ConstraintType.ForeignKey,
                descriptor,
                isSafe,
                safeEnvironments,
                environmentsPresent,
                consensusRatio,
                isSafe
                    ? "FOREIGN KEY constraint can be safely applied across all environments."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "FOREIGN KEY constraint would fail. Environments with orphans: {0}",
                        string.Join(", ", environmentsWithOrphans)));
        }
    }

    private sealed class ForeignKeyKeyComparer : IEqualityComparer<(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn)>
    {
        public static ForeignKeyKeyComparer Instance { get; } = new();

        public bool Equals((string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn) x,
            (string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn) y)
        {
            return string.Equals(x.FromSchema, y.FromSchema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.FromTable, y.FromTable, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.FromColumn, y.FromColumn, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ToSchema, y.ToSchema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ToTable, y.ToTable, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ToColumn, y.ToColumn, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.FromSchema, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.FromTable, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.FromColumn, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.ToSchema, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.ToTable, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.ToColumn, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}

public enum ConstraintType
{
    NotNull,
    Unique,
    CompositeUnique,
    ForeignKey
}

public sealed record ConstraintConsensusResult(
    ConstraintType ConstraintType,
    string ConstraintDescriptor,
    bool IsSafeToApply,
    int SafeEnvironmentCount,
    int TotalEnvironmentCount,
    double ConsensusRatio,
    string Recommendation);

public sealed record ConsensusStatistics(
    int TotalEnvironments,
    int SafeNotNullConstraints,
    int UnsafeNotNullConstraints,
    int SafeUniqueConstraints,
    int UnsafeUniqueConstraints,
    int SafeForeignKeyConstraints,
    int UnsafeForeignKeyConstraints)
{
    public int TotalSafeConstraints => SafeNotNullConstraints + SafeUniqueConstraints + SafeForeignKeyConstraints;
    public int TotalUnsafeConstraints => UnsafeNotNullConstraints + UnsafeUniqueConstraints + UnsafeForeignKeyConstraints;
    public int TotalConstraints => TotalSafeConstraints + TotalUnsafeConstraints;

    public double SafetyRatio => TotalConstraints > 0 ? (double)TotalSafeConstraints / TotalConstraints : 0.0;

    public string FormatSummary()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Analyzed {0} environments: {1}/{2} constraints safe to apply ({3:P1} consensus)",
            TotalEnvironments,
            TotalSafeConstraints,
            TotalConstraints,
            SafetyRatio);
    }
}
