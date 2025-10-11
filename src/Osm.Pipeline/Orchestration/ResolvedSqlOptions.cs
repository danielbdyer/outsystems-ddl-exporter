using System;
using Microsoft.Data.SqlClient;

namespace Osm.Pipeline.Orchestration;

public sealed record ResolvedSqlOptions(
    string? ConnectionString,
    int? CommandTimeoutSeconds,
    SqlSamplingSettings Sampling,
    SqlAuthenticationSettings Authentication,
    SqlProfilerExecutionSettings ProfilerExecution);

public sealed record SqlSamplingSettings(long? RowSamplingThreshold, int? SampleSize);

public sealed record SqlAuthenticationSettings(
    SqlAuthenticationMethod? Method,
    bool? TrustServerCertificate,
    string? ApplicationName,
    string? AccessToken);

public sealed record SqlProfilerExecutionSettings(
    int MaxDegreeOfParallelism,
    int TablesPerBatch,
    int RetryCount,
    TimeSpan RetryBaseDelay,
    TimeSpan RetryJitter)
{
    public static SqlProfilerExecutionSettings Default { get; } = new(4, 32, 3, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.35));
}
