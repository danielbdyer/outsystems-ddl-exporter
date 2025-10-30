using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public sealed record ProfilingRunTelemetry(
    ImmutableArray<TableProfilingTelemetry> Tables,
    ProfilingRunTelemetrySummary Summary)
{
    public static ProfilingRunTelemetry Create(ImmutableArray<TableProfilingTelemetry> tables)
    {
        var normalized = tables.IsDefault ? ImmutableArray<TableProfilingTelemetry>.Empty : tables;
        return new ProfilingRunTelemetry(normalized, ProfilingRunTelemetrySummary.Create(normalized));
    }
}

public sealed record ProfilingRunTelemetrySummary(
    double TotalDurationMilliseconds,
    int TableCount,
    int SampledTableCount,
    int FullScanTableCount,
    ImmutableArray<ProfilingProbeOutcomeSummary> ProbeOutcomes,
    ImmutableArray<ProfilingTableDurationSummary> TopSlowTables)
{
    public static ProfilingRunTelemetrySummary Create(
        ImmutableArray<TableProfilingTelemetry> tables,
        int topSlowTableCount = 5)
    {
        if (tables.IsDefault)
        {
            tables = ImmutableArray<TableProfilingTelemetry>.Empty;
        }

        var tableCount = tables.Length;
        var sampledCount = tables.Count(static table => table.Sampled);
        var fullScanCount = tableCount - sampledCount;
        var totalDurationMs = tables.Sum(static table => table.TotalDurationMilliseconds);

        var topSlowTables = tables
            .OrderByDescending(static table => table.TotalDurationMilliseconds)
            .ThenBy(static table => table.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static table => table.Table, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, topSlowTableCount))
            .Select(table => new ProfilingTableDurationSummary(
                table.Schema,
                table.Table,
                table.Sampled,
                table.SampleSize,
                table.RowCount,
                table.TotalDurationMilliseconds,
                table.NullCountDurationMilliseconds,
                table.UniqueCandidateDurationMilliseconds,
                table.ForeignKeyDurationMilliseconds,
                table.ForeignKeyMetadataDurationMilliseconds))
            .ToImmutableArray();

        var probeOutcomes = BuildProbeOutcomeSummaries(tables);

        return new ProfilingRunTelemetrySummary(
            totalDurationMs,
            tableCount,
            sampledCount,
            fullScanCount,
            probeOutcomes,
            topSlowTables);
    }

    private static ImmutableArray<ProfilingProbeOutcomeSummary> BuildProbeOutcomeSummaries(
        ImmutableArray<TableProfilingTelemetry> tables)
    {
        if (tables.Length == 0)
        {
            return ImmutableArray<ProfilingProbeOutcomeSummary>.Empty;
        }

        var summaries = new List<ProfilingProbeOutcomeSummary>(capacity: 3);
        summaries.Add(BuildOutcomeSummary("nullCounts", tables.Select(static table => table.NullCountStatus)));
        summaries.Add(BuildOutcomeSummary("uniqueCandidates", tables.Select(static table => table.UniqueCandidateStatus)));
        summaries.Add(BuildOutcomeSummary("foreignKeys", tables.Select(static table => table.ForeignKeyStatus)));
        return summaries.ToImmutableArray();
    }

    private static ProfilingProbeOutcomeSummary BuildOutcomeSummary(
        string probe,
        IEnumerable<ProfilingProbeStatus> statuses)
    {
        var outcomes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in statuses)
        {
            var key = status.Outcome.ToString();
            if (!outcomes.TryAdd(key, 1))
            {
                outcomes[key] += 1;
            }
        }

        return new ProfilingProbeOutcomeSummary(probe, outcomes.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase));
    }
}

public sealed record ProfilingProbeOutcomeSummary(
    string Probe,
    ImmutableDictionary<string, int> Outcomes);

public sealed record ProfilingTableDurationSummary(
    string Schema,
    string Table,
    bool Sampled,
    long SampleSize,
    long RowCount,
    double TotalDurationMilliseconds,
    double NullCountDurationMilliseconds,
    double UniqueCandidateDurationMilliseconds,
    double ForeignKeyDurationMilliseconds,
    double ForeignKeyMetadataDurationMilliseconds);
