using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public sealed class MultiTargetSqlDataProfiler : IDataProfiler, IMultiEnvironmentProfiler
{
    private readonly ImmutableArray<ProfilerEnvironment> _targets;
    private readonly int _maxDegreeOfParallelism;

    public MultiEnvironmentProfileReport? Report { get; private set; }

    public MultiTargetSqlDataProfiler(
        ProfilerEnvironment primary,
        IEnumerable<ProfilerEnvironment> secondaries,
        int? maxDegreeOfParallelism = null)
    {
        if (primary is null)
        {
            throw new ArgumentNullException(nameof(primary));
        }

        if (secondaries is null)
        {
            throw new ArgumentNullException(nameof(secondaries));
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
            captures.Select(capture => new ProfilingEnvironmentSnapshot(
                capture.Environment.Name,
                capture.Environment.IsPrimary,
                capture.Environment.LabelOrigin,
                capture.Environment.LabelWasAdjusted,
                capture.Snapshot,
                capture.Duration)));

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
