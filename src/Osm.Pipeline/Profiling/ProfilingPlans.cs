using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

internal sealed record TableProfilingPlan(
    string Schema,
    string Table,
    long RowCount,
    ImmutableArray<string> Columns,
    ImmutableArray<UniqueCandidatePlan> UniqueCandidates,
    ImmutableArray<ForeignKeyPlan> ForeignKeys);

internal sealed record UniqueCandidatePlan(string Key, ImmutableArray<string> Columns);

internal sealed record ForeignKeyPlan(string Key, string Column, string TargetSchema, string TargetTable, string TargetColumn);

internal sealed record TableProfilingResults(
    IReadOnlyDictionary<string, long> NullCounts,
    IReadOnlyDictionary<string, ProfilingProbeStatus> NullCountStatuses,
    IReadOnlyDictionary<string, bool> UniqueDuplicates,
    IReadOnlyDictionary<string, ProfilingProbeStatus> UniqueDuplicateStatuses,
    IReadOnlyDictionary<string, bool> ForeignKeys,
    IReadOnlyDictionary<string, ProfilingProbeStatus> ForeignKeyStatuses,
    IReadOnlyDictionary<string, bool> ForeignKeyIsNoCheck,
    IReadOnlyDictionary<string, ProfilingProbeStatus> ForeignKeyNoCheckStatuses)
{
    public static TableProfilingResults Empty { get; } = new(
        ImmutableDictionary<string, long>.Empty,
        ImmutableDictionary<string, ProfilingProbeStatus>.Empty,
        ImmutableDictionary<string, bool>.Empty,
        ImmutableDictionary<string, ProfilingProbeStatus>.Empty,
        ImmutableDictionary<string, bool>.Empty,
        ImmutableDictionary<string, ProfilingProbeStatus>.Empty,
        ImmutableDictionary<string, bool>.Empty,
        ImmutableDictionary<string, ProfilingProbeStatus>.Empty);
}
