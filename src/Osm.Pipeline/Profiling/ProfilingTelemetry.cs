using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public sealed record TableProfilingTelemetry(
    string Schema,
    string Table,
    long RowCount,
    bool Sampled,
    long SampleSize,
    int SamplingParameter,
    int ColumnCount,
    int UniqueCandidateCount,
    int ForeignKeyCount,
    double NullCountDurationMilliseconds,
    double UniqueCandidateDurationMilliseconds,
    double ForeignKeyDurationMilliseconds,
    double ForeignKeyMetadataDurationMilliseconds,
    double TotalDurationMilliseconds,
    ProfilingProbeStatus NullCountStatus,
    ProfilingProbeStatus UniqueCandidateStatus,
    ProfilingProbeStatus ForeignKeyStatus);

internal sealed class ProfilingTelemetryCollector
{
    private readonly ConcurrentQueue<TableProfilingTelemetry> _entries = new();

    public void Record(TableProfilingTelemetry telemetry)
    {
        if (telemetry is null)
        {
            throw new ArgumentNullException(nameof(telemetry));
        }

        _entries.Enqueue(telemetry);
    }

    public ImmutableArray<TableProfilingTelemetry> ToImmutableArray()
    {
        return _entries.ToImmutableArray();
    }
}
