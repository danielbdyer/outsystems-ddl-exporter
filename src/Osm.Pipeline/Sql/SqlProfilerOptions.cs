using System;

namespace Osm.Pipeline.Sql;

public sealed record SqlProfilerOptions(
    int? CommandTimeoutSeconds,
    SqlSamplingOptions Sampling,
    int MaxDegreeOfParallelism,
    int TableBatchSize,
    int RetryCount,
    TimeSpan RetryBaseDelay,
    TimeSpan RetryJitter)
{
    public static SqlProfilerOptions Default { get; } = new(
        null,
        SqlSamplingOptions.Default,
        MaxDegreeOfParallelism: 1,
        TableBatchSize: 32,
        RetryCount: 3,
        RetryBaseDelay: TimeSpan.FromMilliseconds(200),
        RetryJitter: TimeSpan.FromMilliseconds(100));
}
