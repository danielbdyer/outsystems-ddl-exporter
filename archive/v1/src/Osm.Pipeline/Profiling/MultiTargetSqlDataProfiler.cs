using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

public sealed class MultiTargetSqlDataProfiler : IDataProfiler, IMultiEnvironmentProfiler
{
    private readonly ImmutableArray<ProfilerEnvironment> _targets;
    private readonly int _maxDegreeOfParallelism;
    private readonly double _minimumConsensusThreshold;

    public MultiEnvironmentProfileReport? Report { get; private set; }

    public MultiTargetSqlDataProfiler(
        ProfilerEnvironment primary,
        IEnumerable<ProfilerEnvironment> secondaries,
        int? maxDegreeOfParallelism = null,
        double minimumConsensusThreshold = 1.0)
    {
        if (primary is null)
        {
            throw new ArgumentNullException(nameof(primary));
        }

        if (secondaries is null)
        {
            throw new ArgumentNullException(nameof(secondaries));
        }

        if (minimumConsensusThreshold < 0.0 || minimumConsensusThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConsensusThreshold), "Consensus threshold must be between 0.0 and 1.0");
        }

        var builder = ImmutableArray.CreateBuilder<ProfilerEnvironment>();
        builder.Add(primary);

        foreach (var environment in secondaries)
        {
            if (environment is null)
            {
                continue;
            }

            builder.Add(environment);
        }

        _targets = builder.ToImmutable();
        if (_targets.IsDefaultOrEmpty || _targets.Length == 0)
        {
            throw new ArgumentException("At least one profiler must be provided.", nameof(primary));
        }

        var requestedParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount;
        if (requestedParallelism <= 0)
        {
            requestedParallelism = Environment.ProcessorCount;
        }

        _maxDegreeOfParallelism = Math.Clamp(requestedParallelism, 1, _targets.Length);
        _minimumConsensusThreshold = minimumConsensusThreshold;
    }

    public async Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);

        var captureTasks = new Task<(ProfilerEnvironment Environment, Result<EnvironmentCapture> Result)>[_targets.Length];

        for (var i = 0; i < _targets.Length; i++)
        {
            var target = _targets[i];
            captureTasks[i] = CaptureEnvironmentWithThrottlingAsync(target, semaphore, linkedCts, cancellationToken);
        }

        var captureResults = await Task.WhenAll(captureTasks).ConfigureAwait(false);
        var captures = new List<EnvironmentCapture>(_targets.Length);

        foreach (var (environment, result) in captureResults)
        {
            if (result.IsFailure)
            {
                Report = MultiEnvironmentProfileReport.Empty;
                linkedCts.Cancel();
                return Result<ProfileSnapshot>.Failure(result.Errors);
            }

            captures.Add(result.Value);
        }

        Report = MultiEnvironmentProfileReport.Create(
            captures.Select(capture =>
            {
                var tableNameMappings = capture.Environment.Profiler is ITableNameMappingProvider provider
                    ? provider.TableNameMappings
                    : ImmutableArray<TableNameMapping>.Empty;

                return new ProfilingEnvironmentSnapshot(
                    capture.Environment.Name,
                    capture.Environment.IsPrimary,
                    capture.Environment.LabelOrigin,
                    capture.Environment.LabelWasAdjusted,
                    capture.Snapshot,
                    capture.Duration,
                    tableNameMappings);
            }),
            _minimumConsensusThreshold);

        if (captures.Count == 1)
        {
            return Result<ProfileSnapshot>.Success(captures[0].Snapshot);
        }

        return MergeSnapshots(captures.Select(capture => capture.Snapshot).ToArray());
    }

    private async Task<(ProfilerEnvironment Environment, Result<EnvironmentCapture> Result)> CaptureEnvironmentWithThrottlingAsync(
        ProfilerEnvironment environment,
        SemaphoreSlim semaphore,
        CancellationTokenSource linkedCts,
        CancellationToken originalToken)
    {
        await semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);

        try
        {
            if (linkedCts.IsCancellationRequested && !originalToken.IsCancellationRequested)
            {
                return (environment, Result<EnvironmentCapture>.Failure(
                    ValidationError.Create(
                        "pipeline.profiling.cancelled",
                        $"Profiling for '{environment.Name}' was cancelled after a failure in another environment.")));
            }

            var result = await CaptureEnvironmentAsync(environment, linkedCts.Token).ConfigureAwait(false);
            if (result.Result.IsFailure && !linkedCts.IsCancellationRequested)
            {
                linkedCts.Cancel();
            }

            return result;
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested && !originalToken.IsCancellationRequested)
        {
            return (environment, Result<EnvironmentCapture>.Failure(
                ValidationError.Create(
                    "pipeline.profiling.cancelled",
                    $"Profiling for '{environment.Name}' was cancelled after a failure in another environment.")));
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task<(ProfilerEnvironment Environment, Result<EnvironmentCapture> Result)> CaptureEnvironmentAsync(
        ProfilerEnvironment environment,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshotResult = await environment.Profiler.CaptureAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (snapshotResult.IsFailure)
        {
            return (environment, Result<EnvironmentCapture>.Failure(snapshotResult.Errors));
        }

        var capture = new EnvironmentCapture(environment, snapshotResult.Value, stopwatch.Elapsed);
        return (environment, Result<EnvironmentCapture>.Success(capture));
    }

    private static Result<ProfileSnapshot> MergeSnapshots(IReadOnlyList<ProfileSnapshot> snapshots)
    {
        // Use worst-case aggregation strategy for multi-environment constraint safety
        // This ensures constraints are only applied if they're safe across ALL environments
        var columnMap = new Dictionary<(string Schema, string Table, string Column), ColumnProfile>(ColumnKeyComparer.Instance);
        var uniqueMap = new Dictionary<(string Schema, string Table, string Column), UniqueCandidateProfile>(ColumnKeyComparer.Instance);
        var compositeMap = new Dictionary<(string Schema, string Table, string Key), CompositeUniqueCandidateProfile>(CompositeUniqueKeyComparer.Instance);
        var foreignKeyMap = new Dictionary<(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn), ForeignKeyReality>(ForeignKeyKeyComparer.Instance);

        foreach (var snapshot in snapshots)
        {
            MergeColumnsWithAggregation(snapshot, columnMap);
            MergeUniqueCandidatesWithAggregation(snapshot, uniqueMap);
            MergeCompositeCandidatesWithAggregation(snapshot, compositeMap);
            MergeForeignKeysWithAggregation(snapshot, foreignKeyMap);
        }

        return ProfileSnapshot.Create(
            columnMap.Values,
            uniqueMap.Values,
            compositeMap.Values,
            foreignKeyMap.Values);
    }

    private static void MergeColumnsWithAggregation(
        ProfileSnapshot snapshot,
        IDictionary<(string Schema, string Table, string Column), ColumnProfile> map)
    {
        foreach (var column in snapshot.Columns)
        {
            var key = (column.Schema.Value, column.Table.Value, column.Column.Value);

            if (!map.TryGetValue(key, out var existing))
            {
                // First occurrence - add as-is
                map[key] = column;
                continue;
            }

            // Aggregate: use maximum NULL count (most conservative for constraint application)
            // CRITICAL: Also use maximum row count to prevent validation failures
            // If Environment A has 1000 rows and Environment B has 2000 rows with 1500 NULLs,
            // we must use 2000 as row count to allow 1500 NULLs to validate
            var maxRowCount = Math.Max(existing.RowCount, column.RowCount);
            var baseNullCount = Math.Max(existing.NullCount, column.NullCount);
            var worstProbeStatus = AggregateProbeStatus(existing.NullCountStatus, column.NullCountStatus);

            // Aggregate null row samples if present in either environment and normalize totals
            var (aggregatedSample, aggregatedNullTotal) = AggregateNullRowSample(
                existing.NullRowSample,
                column.NullRowSample,
                baseNullCount);

            var maxNullCount = Math.Max(baseNullCount, aggregatedNullTotal);
            if (aggregatedSample is not null)
            {
                maxNullCount = Math.Max(maxNullCount, aggregatedSample.TotalNullRows);
            }

            maxRowCount = Math.Max(maxRowCount, maxNullCount);

            // Use first occurrence's metadata (physical nullability, PK, etc.) as baseline
            // but aggregate data quality metrics (row count, null count, probe status)
            if (maxRowCount != existing.RowCount ||
                maxNullCount != existing.NullCount ||
                worstProbeStatus != existing.NullCountStatus ||
                !Equals(aggregatedSample, existing.NullRowSample))
            {
                var aggregated = ColumnProfile.Create(
                    existing.Schema,
                    existing.Table,
                    existing.Column,
                    existing.IsNullablePhysical,
                    existing.IsComputed,
                    existing.IsPrimaryKey,
                    existing.IsUniqueKey,
                    existing.DefaultDefinition,
                    maxRowCount,       // Use max row count to accommodate max null count
                    maxNullCount,      // Use worst case across environments
                    worstProbeStatus,  // Use worst probe outcome
                    aggregatedSample);

                if (aggregated.IsSuccess)
                {
                    map[key] = aggregated.Value;
                }
                else
                {
                    // Log validation failure but keep existing to prevent data loss
                    // This should not happen with max row count, but defensive coding
                }
            }
        }
    }

    private static (NullRowSample? Sample, long TotalNullRows) AggregateNullRowSample(
        NullRowSample? first,
        NullRowSample? second,
        long baseNullCount)
    {
        if (first is null && second is null)
        {
            return (null, baseNullCount);
        }

        if (first is null)
        {
            return NormalizeNullRowSample(second!, baseNullCount);
        }

        if (second is null)
        {
            return NormalizeNullRowSample(first, baseNullCount);
        }

        var primaryKeyColumns = !second.PrimaryKeyColumns.IsDefaultOrEmpty
            ? second.PrimaryKeyColumns
            : first.PrimaryKeyColumns;

        if (primaryKeyColumns.IsDefaultOrEmpty)
        {
            primaryKeyColumns = first.PrimaryKeyColumns;
        }

        var combinedRows = first.SampleRows
            .Concat(second.SampleRows)
            .Distinct(NullRowIdentifierComparer.Instance)
            .ToImmutableArray();

        var totalNullRows = Math.Max(baseNullCount, Math.Max(first.TotalNullRows, second.TotalNullRows));
        totalNullRows = Math.Max(totalNullRows, combinedRows.Length);

        var sample = NullRowSample.Create(primaryKeyColumns, combinedRows, totalNullRows);
        return (sample, totalNullRows);
    }

    private static (NullRowSample Sample, long TotalNullRows) NormalizeNullRowSample(
        NullRowSample sample,
        long baseNullCount)
    {
        var totalNullRows = Math.Max(baseNullCount, Math.Max(sample.TotalNullRows, sample.SampleRows.Length));

        if (totalNullRows == sample.TotalNullRows)
        {
            return (sample, totalNullRows);
        }

        var normalized = NullRowSample.Create(sample.PrimaryKeyColumns, sample.SampleRows, totalNullRows);
        return (normalized, totalNullRows);
    }

    private static void MergeUniqueCandidatesWithAggregation(
        ProfileSnapshot snapshot,
        IDictionary<(string Schema, string Table, string Column), UniqueCandidateProfile> map)
    {
        foreach (var profile in snapshot.UniqueCandidates)
        {
            var key = (profile.Schema.Value, profile.Table.Value, profile.Column.Value);

            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = profile;
                continue;
            }

            // Aggregate: if ANY environment has duplicates, mark as having duplicates
            // This is conservative for constraint application
            var hasDuplicate = existing.HasDuplicate || profile.HasDuplicate;
            var worstProbeStatus = AggregateProbeStatus(existing.ProbeStatus, profile.ProbeStatus);

            if (hasDuplicate != existing.HasDuplicate || worstProbeStatus != existing.ProbeStatus)
            {
                var aggregated = UniqueCandidateProfile.Create(
                    existing.Schema,
                    existing.Table,
                    existing.Column,
                    hasDuplicate,
                    worstProbeStatus);

                if (aggregated.IsSuccess)
                {
                    map[key] = aggregated.Value;
                }
            }
        }
    }

    private static void MergeCompositeCandidatesWithAggregation(
        ProfileSnapshot snapshot,
        IDictionary<(string Schema, string Table, string Key), CompositeUniqueCandidateProfile> map)
    {
        foreach (var profile in snapshot.CompositeUniqueCandidates)
        {
            var key = (
                profile.Schema.Value,
                profile.Table.Value,
                ProfilingPlanBuilder.BuildUniqueKey(profile.Columns.Select(static column => column.Value)));

            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = profile;
                continue;
            }

            // Aggregate: if ANY environment has duplicates, mark as having duplicates
            // NOTE: CompositeUniqueCandidateProfile does not have ProbeStatus field (unlike UniqueCandidateProfile)
            var hasDuplicate = existing.HasDuplicate || profile.HasDuplicate;

            if (hasDuplicate != existing.HasDuplicate)
            {
                var aggregated = CompositeUniqueCandidateProfile.Create(
                    existing.Schema,
                    existing.Table,
                    existing.Columns,  // Use first occurrence's column order
                    hasDuplicate);

                if (aggregated.IsSuccess)
                {
                    map[key] = aggregated.Value;
                }
            }
        }
    }

    private static void MergeForeignKeysWithAggregation(
        ProfileSnapshot snapshot,
        IDictionary<(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn), ForeignKeyReality> map)
    {
        foreach (var reality in snapshot.ForeignKeys)
        {
            var reference = reality.Reference;
            var key = (
                reference.FromSchema.Value,
                reference.FromTable.Value,
                reference.FromColumn.Value,
                reference.ToSchema.Value,
                reference.ToTable.Value,
                reference.ToColumn.Value);

            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = reality;
                continue;
            }

            // Aggregate: if ANY environment has orphans, mark as having orphans
            // Aggregate NOCHECK status (if any environment has NOCHECK)
            var hasOrphan = existing.HasOrphan || reality.HasOrphan;
            var isNoCheck = existing.IsNoCheck || reality.IsNoCheck;
            var worstProbeStatus = AggregateProbeStatus(existing.ProbeStatus, reality.ProbeStatus);
            var (aggregatedSample, aggregatedCount) = AggregateOrphanSample(
                existing.OrphanSample,
                reality.OrphanSample,
                existing.OrphanCount,
                reality.OrphanCount);

            hasOrphan = hasOrphan || aggregatedCount > 0;

            if (hasOrphan != existing.HasOrphan ||
                aggregatedCount != existing.OrphanCount ||
                isNoCheck != existing.IsNoCheck ||
                worstProbeStatus != existing.ProbeStatus ||
                !Equals(aggregatedSample, existing.OrphanSample))
            {
                var aggregated = ForeignKeyReality.Create(
                    existing.Reference,  // Use first occurrence's reference
                    hasOrphan,
                    aggregatedCount,
                    isNoCheck,
                    worstProbeStatus,
                    aggregatedSample);

                if (aggregated.IsSuccess)
                {
                    map[key] = aggregated.Value;
                }
            }
        }
    }

    private static (ForeignKeyOrphanSample? Sample, long TotalOrphans) AggregateOrphanSample(
        ForeignKeyOrphanSample? first,
        ForeignKeyOrphanSample? second,
        long firstCount,
        long secondCount)
    {
        var baseCount = Math.Max(firstCount, secondCount);

        if (first is null && second is null)
        {
            return (null, baseCount);
        }

        if (first is null)
        {
            return NormalizeOrphanSample(second!, baseCount);
        }

        if (second is null)
        {
            return NormalizeOrphanSample(first, baseCount);
        }

        var primaryKeyColumns = !second.PrimaryKeyColumns.IsDefaultOrEmpty
            ? second.PrimaryKeyColumns
            : first.PrimaryKeyColumns;

        if (primaryKeyColumns.IsDefaultOrEmpty)
        {
            primaryKeyColumns = first.PrimaryKeyColumns;
        }

        var foreignKeyColumn = string.IsNullOrWhiteSpace(second.ForeignKeyColumn)
            ? first.ForeignKeyColumn
            : second.ForeignKeyColumn;

        var combinedRows = first.SampleRows
            .Concat(second.SampleRows)
            .Distinct(ForeignKeyOrphanIdentifierComparer.Instance)
            .ToImmutableArray();

        var totalOrphans = Math.Max(baseCount, Math.Max(first.TotalOrphans, second.TotalOrphans));
        totalOrphans = Math.Max(totalOrphans, combinedRows.Length);

        var sample = ForeignKeyOrphanSample.Create(primaryKeyColumns, foreignKeyColumn, combinedRows, totalOrphans);
        return (sample, totalOrphans);
    }

    private static (ForeignKeyOrphanSample Sample, long TotalOrphans) NormalizeOrphanSample(
        ForeignKeyOrphanSample sample,
        long baseCount)
    {
        var totalOrphans = Math.Max(baseCount, Math.Max(sample.TotalOrphans, sample.SampleRows.Length));

        if (totalOrphans == sample.TotalOrphans)
        {
            return (sample, totalOrphans);
        }

        var normalized = ForeignKeyOrphanSample.Create(sample.PrimaryKeyColumns, sample.ForeignKeyColumn, sample.SampleRows, totalOrphans);
        return (normalized, totalOrphans);
    }

    private sealed class NullRowIdentifierComparer : IEqualityComparer<NullRowIdentifier>
    {
        public static NullRowIdentifierComparer Instance { get; } = new();

        public bool Equals(NullRowIdentifier? x, NullRowIdentifier? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (x.PrimaryKeyValues.Length != y.PrimaryKeyValues.Length)
            {
                return false;
            }

            for (var i = 0; i < x.PrimaryKeyValues.Length; i++)
            {
                if (!Equals(x.PrimaryKeyValues[i], y.PrimaryKeyValues[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(NullRowIdentifier obj)
        {
            var hash = new HashCode();

            foreach (var value in obj.PrimaryKeyValues)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }

    private sealed class ForeignKeyOrphanIdentifierComparer : IEqualityComparer<ForeignKeyOrphanIdentifier>
    {
        public static ForeignKeyOrphanIdentifierComparer Instance { get; } = new();

        public bool Equals(ForeignKeyOrphanIdentifier? x, ForeignKeyOrphanIdentifier? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (x.PrimaryKeyValues.Length != y.PrimaryKeyValues.Length)
            {
                return false;
            }

            for (var i = 0; i < x.PrimaryKeyValues.Length; i++)
            {
                if (!Equals(x.PrimaryKeyValues[i], y.PrimaryKeyValues[i]))
                {
                    return false;
                }
            }

            return Equals(x.ForeignKeyValue, y.ForeignKeyValue);
        }

        public int GetHashCode(ForeignKeyOrphanIdentifier obj)
        {
            var hash = new HashCode();

            foreach (var value in obj.PrimaryKeyValues)
            {
                hash.Add(value);
            }

            hash.Add(obj.ForeignKeyValue);

            return hash.ToHashCode();
        }
    }

    private static ProfilingProbeStatus AggregateProbeStatus(ProfilingProbeStatus first, ProfilingProbeStatus second)
    {
        // Null safety: if either is null, return the non-null one or Unknown
        if (first is null && second is null)
        {
            return ProfilingProbeStatus.Unknown;
        }

        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        // Aggregate probe statuses: use the worst outcome for conservative constraint application
        // Priority: Unknown > AmbiguousMapping > Cancelled > FallbackTimeout > Succeeded/TrustedConstraint
        // Unknown is worst because we have no information about the data
        if (first.Outcome == ProfilingProbeOutcome.Unknown || second.Outcome == ProfilingProbeOutcome.Unknown)
        {
            return first.Outcome == ProfilingProbeOutcome.Unknown ? first : second;
        }

        if (first.Outcome == ProfilingProbeOutcome.AmbiguousMapping || second.Outcome == ProfilingProbeOutcome.AmbiguousMapping)
        {
            return first.Outcome == ProfilingProbeOutcome.AmbiguousMapping ? first : second;
        }

        if (first.Outcome == ProfilingProbeOutcome.Cancelled || second.Outcome == ProfilingProbeOutcome.Cancelled)
        {
            return first.Outcome == ProfilingProbeOutcome.Cancelled ? first : second;
        }

        if (first.Outcome == ProfilingProbeOutcome.FallbackTimeout || second.Outcome == ProfilingProbeOutcome.FallbackTimeout)
        {
            return first.Outcome == ProfilingProbeOutcome.FallbackTimeout ? first : second;
        }

        if (first.Outcome == ProfilingProbeOutcome.TrustedConstraint || second.Outcome == ProfilingProbeOutcome.TrustedConstraint)
        {
            return first.Outcome == ProfilingProbeOutcome.TrustedConstraint ? first : second;
        }

        // Both succeeded - return first (they're equivalent)
        return first;
    }

    private sealed class CompositeUniqueKeyComparer : IEqualityComparer<(string Schema, string Table, string Key)>
    {
        public static CompositeUniqueKeyComparer Instance { get; } = new();

        public bool Equals((string Schema, string Table, string Key) x, (string Schema, string Table, string Key) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Schema, string Table, string Key) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Key, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed class ForeignKeyKeyComparer : IEqualityComparer<(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn)>
    {
        public static ForeignKeyKeyComparer Instance { get; } = new();

        public bool Equals((string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn) x, (string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn) y)
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

    private sealed record EnvironmentCapture(ProfilerEnvironment Environment, ProfileSnapshot Snapshot, TimeSpan Duration);

    public sealed class ProfilerEnvironment
    {
        public ProfilerEnvironment(
            string name,
            IDataProfiler profiler,
            bool isPrimary,
            EnvironmentLabelOrigin labelOrigin = EnvironmentLabelOrigin.Provided,
            bool labelWasAdjusted = false)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Environment name must be provided.", nameof(name));
            }

            Profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
            Name = name.Trim();
            if (Name.Length == 0)
            {
                throw new ArgumentException("Environment name must be provided.", nameof(name));
            }

            IsPrimary = isPrimary;
            LabelOrigin = labelOrigin;
            LabelWasAdjusted = labelWasAdjusted;
        }

        public string Name { get; }

        public IDataProfiler Profiler { get; }

        public bool IsPrimary { get; }

        public EnvironmentLabelOrigin LabelOrigin { get; }

        public bool LabelWasAdjusted { get; }
    }

    public enum EnvironmentLabelOrigin
    {
        Provided,
        DerivedFromDatabase,
        DerivedFromApplicationName,
        DerivedFromDataSource,
        Fallback
    }
}
