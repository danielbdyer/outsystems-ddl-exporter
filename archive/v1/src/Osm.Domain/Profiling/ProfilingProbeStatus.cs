using System;

namespace Osm.Domain.Profiling;

public enum ProfilingProbeOutcome
{
    Unknown = 0,
    Succeeded,
    FallbackTimeout,
    Cancelled,
    TrustedConstraint,
    AmbiguousMapping
}

public sealed record ProfilingProbeStatus(
    DateTimeOffset CapturedAtUtc,
    long SampleSize,
    ProfilingProbeOutcome Outcome)
{
    public static ProfilingProbeStatus Unknown { get; } = new(DateTimeOffset.UnixEpoch, 0, ProfilingProbeOutcome.Unknown);

    public static ProfilingProbeStatus CreateSucceeded(DateTimeOffset capturedAtUtc, long sampleSize)
    {
        return new ProfilingProbeStatus(capturedAtUtc, sampleSize, ProfilingProbeOutcome.Succeeded);
    }

    public static ProfilingProbeStatus CreateFallbackTimeout(DateTimeOffset capturedAtUtc, long sampleSize)
    {
        return new ProfilingProbeStatus(capturedAtUtc, sampleSize, ProfilingProbeOutcome.FallbackTimeout);
    }

    public static ProfilingProbeStatus CreateCancelled(DateTimeOffset capturedAtUtc, long sampleSize)
    {
        return new ProfilingProbeStatus(capturedAtUtc, sampleSize, ProfilingProbeOutcome.Cancelled);
    }
}
