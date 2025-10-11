using Microsoft.Data.SqlClient;

namespace Osm.Pipeline.Orchestration;

public sealed record ResolvedSqlOptions(
    string? ConnectionString,
    int? CommandTimeoutSeconds,
    SqlSamplingSettings Sampling,
    SqlAuthenticationSettings Authentication,
    int? MaxDegreeOfParallelism,
    int? TableBatchSize,
    int? RetryCount,
    int? RetryBaseDelayMilliseconds,
    int? RetryJitterMilliseconds);

public sealed record SqlSamplingSettings(long? RowSamplingThreshold, int? SampleSize);

public sealed record SqlAuthenticationSettings(
    SqlAuthenticationMethod? Method,
    bool? TrustServerCertificate,
    string? ApplicationName,
    string? AccessToken);
