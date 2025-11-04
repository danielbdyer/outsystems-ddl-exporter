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
    ImmutableArray<TableNameMappingConfiguration> TableNameMappings,
    double? MinimumConsensusThreshold = null)
{
    /// <summary>
    /// Gets the consensus threshold for multi-environment constraint analysis.
    /// Must be between 0.0 (0%) and 1.0 (100%). Defaults to 1.0 (100% agreement required).
    /// A value of 1.0 means constraints must be safe in ALL environments.
    /// A value of 0.8 means constraints must be safe in at least 80% of environments.
    /// </summary>
    public double EffectiveConsensusThreshold => MinimumConsensusThreshold switch
    {
        null => 1.0,  // Default: require 100% consensus
        < 0.0 => 0.0,
        > 1.0 => 1.0,
        double value => value
    };
}

public sealed record SqlSamplingSettings(long? RowSamplingThreshold, int? SampleSize);

public sealed record SqlAuthenticationSettings(
    SqlAuthenticationMethod? Method,
    bool? TrustServerCertificate,
    string? ApplicationName,
    string? AccessToken);
