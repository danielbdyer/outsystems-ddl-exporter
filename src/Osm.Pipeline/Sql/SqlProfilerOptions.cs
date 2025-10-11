using System;

namespace Osm.Pipeline.Sql;

public sealed record SqlProfilerOptions(
    int? CommandTimeoutSeconds,
    SqlSamplingOptions Sampling,
    int MaxDegreeOfParallelism,
    int TablesPerBatch,
    SqlRetryPolicyOptions RetryPolicy)
{
    public static SqlProfilerOptions Default { get; } = new(null, SqlSamplingOptions.Default, 4, 32, SqlRetryPolicyOptions.Default);
}

public sealed record SqlRetryPolicyOptions(int RetryCount, TimeSpan BaseDelay, TimeSpan Jitter)
{
    public static SqlRetryPolicyOptions Default { get; } = new(3, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.35));
}
