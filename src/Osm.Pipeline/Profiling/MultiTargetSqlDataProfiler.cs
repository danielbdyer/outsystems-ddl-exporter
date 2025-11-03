using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public sealed class MultiTargetSqlDataProfiler : IDataProfiler
{
    private readonly IDataProfiler _primary;
    private readonly ImmutableArray<IDataProfiler> _secondaries;

    public MultiTargetSqlDataProfiler(IDataProfiler primary, IEnumerable<IDataProfiler> secondaries)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        if (secondaries is null)
        {
            throw new ArgumentNullException(nameof(secondaries));
        }

        _secondaries = secondaries
            .Where(static profiler => profiler is not null)
            .Cast<IDataProfiler>()
            .ToImmutableArray();
    }

    public async Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = new List<ProfileSnapshot>(_secondaries.Length + 1);

        var primaryResult = await _primary.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (primaryResult.IsFailure)
        {
            return Result<ProfileSnapshot>.Failure(primaryResult.Errors);
        }

        snapshots.Add(primaryResult.Value);

        foreach (var profiler in _secondaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await profiler.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result<ProfileSnapshot>.Failure(result.Errors);
            }

            snapshots.Add(result.Value);
        }

        return snapshots.Count == 1
            ? primaryResult
            : MergeSnapshots(snapshots);
    }

    private static Result<ProfileSnapshot> MergeSnapshots(IReadOnlyList<ProfileSnapshot> snapshots)
    {
        var columnMap = new Dictionary<(string Schema, string Table, string Column), ColumnProfile>(ColumnKeyComparer.Instance);
        var uniqueMap = new Dictionary<(string Schema, string Table, string Column), UniqueCandidateProfile>(ColumnKeyComparer.Instance);
        var compositeMap = new Dictionary<(string Schema, string Table, string Key), CompositeUniqueCandidateProfile>(CompositeUniqueKeyComparer.Instance);
        var foreignKeyMap = new Dictionary<(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn), ForeignKeyReality>(ForeignKeyKeyComparer.Instance);

        foreach (var snapshot in snapshots)
        {
            MergeColumns(snapshot, columnMap);
            MergeUniqueCandidates(snapshot, uniqueMap);
            MergeCompositeCandidates(snapshot, compositeMap);
            MergeForeignKeys(snapshot, foreignKeyMap);
        }

        return ProfileSnapshot.Create(
            columnMap.Values,
            uniqueMap.Values,
            compositeMap.Values,
            foreignKeyMap.Values);
    }

    private static void MergeColumns(ProfileSnapshot snapshot, IDictionary<(string Schema, string Table, string Column), ColumnProfile> map)
    {
        foreach (var column in snapshot.Columns)
        {
            var key = (column.Schema.Value, column.Table.Value, column.Column.Value);
            if (!map.ContainsKey(key))
            {
                map[key] = column;
            }
        }
    }

    private static void MergeUniqueCandidates(ProfileSnapshot snapshot, IDictionary<(string Schema, string Table, string Column), UniqueCandidateProfile> map)
    {
        foreach (var profile in snapshot.UniqueCandidates)
        {
            var key = (profile.Schema.Value, profile.Table.Value, profile.Column.Value);
            if (!map.ContainsKey(key))
            {
                map[key] = profile;
            }
        }
    }

    private static void MergeCompositeCandidates(ProfileSnapshot snapshot, IDictionary<(string Schema, string Table, string Key), CompositeUniqueCandidateProfile> map)
    {
        foreach (var profile in snapshot.CompositeUniqueCandidates)
        {
            var key = (
                profile.Schema.Value,
                profile.Table.Value,
                ProfilingPlanBuilder.BuildUniqueKey(profile.Columns.Select(static column => column.Value)));

            if (!map.ContainsKey(key))
            {
                map[key] = profile;
            }
        }
    }

    private static void MergeForeignKeys(ProfileSnapshot snapshot, IDictionary<(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn), ForeignKeyReality> map)
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

            if (!map.ContainsKey(key))
            {
                map[key] = reality;
            }
        }
    }

    private sealed class CompositeUniqueKeyComparer : IEqualityComparer<(string Schema, string Table, string Key)>
    {
        public static CompositeUniqueKeyComparer Instance { get; } = new();

        public bool Equals((string Schema, string Table, string Key) x, (string Schema, string Table, string Key) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Key, y.Key, StringComparison.Ordinal);
        }

        public int GetHashCode((string Schema, string Table, string Key) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Key, StringComparer.Ordinal);
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
}
