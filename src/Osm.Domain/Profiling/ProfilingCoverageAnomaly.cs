using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Domain.Profiling;

public enum ProfilingCoverageAnomalyType
{
    ColumnMetadataMissing,
    NullCountProbeMissing,
    UniqueProbeMissing,
    CompositeUniqueProbeMissing,
    ForeignKeyProbeMissing
}

public sealed record ProfilingCoverageAnomaly(
    ProfilingCoverageAnomalyType Type,
    string Message,
    string RemediationHint,
    ProfilingInsightCoordinate Coordinate,
    ImmutableArray<string> Columns,
    ProfilingProbeOutcome Outcome)
{
    public static ProfilingCoverageAnomaly Create(
        ProfilingCoverageAnomalyType type,
        string message,
        string remediationHint,
        ProfilingInsightCoordinate coordinate,
        IEnumerable<string>? columns,
        ProfilingProbeOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Coverage anomaly message must be provided.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(remediationHint))
        {
            throw new ArgumentException("Coverage anomaly remediation hint must be provided.", nameof(remediationHint));
        }

        var columnArray = columns is null
            ? ImmutableArray<string>.Empty
            : columns.ToImmutableArray();

        return new ProfilingCoverageAnomaly(
            type,
            message.Trim(),
            remediationHint.Trim(),
            coordinate ?? throw new ArgumentNullException(nameof(coordinate)),
            columnArray,
            outcome);
    }
}
