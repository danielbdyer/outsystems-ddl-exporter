using System;
using System.Collections.Generic;
using Osm.Domain.Configuration;

namespace Osm.Cli.Configuration;

public sealed record CliConfiguration(
    TighteningOptions Tightening,
    string? ModelPath,
    string? ProfilePath,
    string? DmmPath,
    CacheConfiguration Cache,
    ProfilerConfiguration Profiler,
    SqlConfiguration Sql,
    ModuleFilterConfiguration ModuleFilter)
{
    public static CliConfiguration Empty { get; } = new(
        TighteningOptions.Default,
        ModelPath: null,
        ProfilePath: null,
        DmmPath: null,
        CacheConfiguration.Empty,
        ProfilerConfiguration.Empty,
        SqlConfiguration.Empty,
        ModuleFilterConfiguration.Empty);
}

public sealed record CacheConfiguration(string? Root, bool? Refresh)
{
    public static CacheConfiguration Empty { get; } = new(null, null);
}

public sealed record ProfilerConfiguration(string? Provider, string? ProfilePath, string? MockFolder)
{
    public static ProfilerConfiguration Empty { get; } = new(null, null, null);
}

public sealed record SqlConfiguration(string? ConnectionString, int? CommandTimeoutSeconds)
{
    public static SqlConfiguration Empty { get; } = new(null, null);
}

public sealed record ModuleFilterConfiguration(
    IReadOnlyList<string> Modules,
    bool? IncludeSystemModules,
    bool? IncludeInactiveModules)
{
    public static ModuleFilterConfiguration Empty { get; }
        = new ModuleFilterConfiguration(Array.Empty<string>(), null, null);
}
