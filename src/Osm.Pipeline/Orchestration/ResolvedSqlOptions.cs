using System.Collections.Immutable;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Orchestration;

public sealed record ResolvedSqlOptions(
    string? ConnectionString,
    int? CommandTimeoutSeconds,
    SqlSamplingSettings Sampling,
    SqlAuthenticationSettings Authentication,
    MetadataContractOverrides MetadataContract,
    ImmutableArray<string> ProfilingConnectionStrings,
    ImmutableArray<TableNameMappingConfiguration> TableNameMappings);

public sealed record SqlSamplingSettings(long? RowSamplingThreshold, int? SampleSize);

public sealed record SqlAuthenticationSettings(
    SqlAuthenticationMethod? Method,
    bool? TrustServerCertificate,
    string? ApplicationName,
    string? AccessToken);
