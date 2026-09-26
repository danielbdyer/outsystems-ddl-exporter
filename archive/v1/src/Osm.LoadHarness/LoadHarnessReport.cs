using System;
using System.Collections.Immutable;

namespace Osm.LoadHarness;

public sealed record BatchTiming(int BatchNumber, TimeSpan Duration);

public sealed record WaitStatDelta(string WaitType, long DeltaMilliseconds);

public sealed record LockSummaryEntry(string RequestMode, string ResourceType, int Count);

public sealed record IndexFragmentationEntry(
    string SchemaName,
    string ObjectName,
    string IndexName,
    double AverageFragmentationPercent,
    long PageCount);

public sealed record ScriptReplayResult(
    ScriptReplayCategory Category,
    string ScriptPath,
    int BatchCount,
    TimeSpan Duration,
    ImmutableArray<BatchTiming> BatchTimings,
    ImmutableArray<WaitStatDelta> WaitStats,
    ImmutableArray<LockSummaryEntry> LockSummary,
    ImmutableArray<IndexFragmentationEntry> IndexFragmentation,
    ImmutableArray<string> Warnings);

public sealed record LoadHarnessReport(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    ImmutableArray<ScriptReplayResult> Scripts,
    TimeSpan TotalDuration)
{
    public static LoadHarnessReport Empty() => new(
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        ImmutableArray<ScriptReplayResult>.Empty,
        TimeSpan.Zero);
}
