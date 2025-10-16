using System;
using Osm.Domain.Configuration;

namespace Osm.Pipeline.Sql;

public sealed record SqlProfilerOptions
{
    public SqlProfilerOptions(
        int? commandTimeoutSeconds,
        SqlSamplingOptions sampling,
        int maxConcurrentTableProfiles,
        SqlProfilerLimits limits,
        NamingOverrideOptions namingOverrides)
    {
        Sampling = sampling ?? throw new ArgumentNullException(nameof(sampling));
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        NamingOverrides = namingOverrides ?? throw new ArgumentNullException(nameof(namingOverrides));

        if (maxConcurrentTableProfiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentTableProfiles), "Concurrency must be positive.");
        }

        CommandTimeoutSeconds = commandTimeoutSeconds;
        MaxConcurrentTableProfiles = maxConcurrentTableProfiles;
    }

    public int? CommandTimeoutSeconds { get; init; }

    public SqlSamplingOptions Sampling { get; init; }

    public int MaxConcurrentTableProfiles { get; init; }

    public SqlProfilerLimits Limits { get; init; }

    public NamingOverrideOptions NamingOverrides { get; init; }

    public static SqlProfilerOptions Default { get; } = new(
        null,
        SqlSamplingOptions.Default,
        4,
        SqlProfilerLimits.Default,
        NamingOverrideOptions.Empty);
}
