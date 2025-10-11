using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Osm.Domain.Configuration;

namespace Osm.App.Configuration;

public sealed record CliConfiguration(
    TighteningOptions Tightening,
    string? ModelPath,
    string? ProfilePath,
    string? DmmPath,
    CacheConfiguration Cache,
    ProfilerConfiguration Profiler,
    SqlConfiguration Sql,
    ModuleFilterConfiguration ModuleFilter,
    SupplementalModelConfiguration SupplementalModels)
{
    public static CliConfiguration Empty { get; } = new(
        TighteningOptions.Default,
        ModelPath: null,
        ProfilePath: null,
        DmmPath: null,
        CacheConfiguration.Empty,
        ProfilerConfiguration.Empty,
        SqlConfiguration.Empty,
        ModuleFilterConfiguration.Empty,
        SupplementalModelConfiguration.Empty);
}

public sealed record CacheConfiguration(string? Root, bool? Refresh)
{
    public static CacheConfiguration Empty { get; } = new(null, null);
}

public sealed record ProfilerConfiguration(string? Provider, string? ProfilePath, string? MockFolder)
{
    public static ProfilerConfiguration Empty { get; } = new(null, null, null);
}

public sealed record SqlConfiguration(
    string? ConnectionString,
    int? CommandTimeoutSeconds,
    SqlSamplingConfiguration Sampling,
    SqlAuthenticationConfiguration Authentication)
{
    public static SqlConfiguration Empty { get; } = new(null, null, SqlSamplingConfiguration.Empty, SqlAuthenticationConfiguration.Empty);
}

public sealed record SqlSamplingConfiguration(long? RowSamplingThreshold, int? SampleSize)
{
    public static SqlSamplingConfiguration Empty { get; } = new(null, null);
}

public sealed record SqlAuthenticationConfiguration(
    SqlAuthenticationMethod? Method,
    bool? TrustServerCertificate,
    string? ApplicationName,
    string? AccessToken)
{
    public static SqlAuthenticationConfiguration Empty { get; } = new(null, null, null, null);
}

public sealed record ModuleFilterConfiguration(
    IReadOnlyList<string> Modules,
    bool? IncludeSystemModules,
    bool? IncludeInactiveModules)
{
    public static ModuleFilterConfiguration Empty { get; }
        = new ModuleFilterConfiguration(Array.Empty<string>(), null, null);
}

public sealed record SupplementalModelConfiguration(
    bool? IncludeUsers,
    IReadOnlyList<string> Paths)
{
    public static SupplementalModelConfiguration Empty { get; }
        = new SupplementalModelConfiguration(true, Array.Empty<string>());
}
