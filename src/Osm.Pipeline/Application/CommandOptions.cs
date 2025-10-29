using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Osm.Domain.Configuration;
using Osm.Pipeline.ModelIngestion;

namespace Osm.Pipeline.Application;

public sealed record SqlOptionsOverrides(
    string? ConnectionString,
    int? CommandTimeoutSeconds,
    long? SamplingThreshold,
    int? SamplingSize,
    SqlAuthenticationMethod? AuthenticationMethod,
    bool? TrustServerCertificate,
    string? ApplicationName,
    string? AccessToken);

public sealed record ModuleFilterOverrides(
    IReadOnlyList<string> Modules,
    bool? IncludeSystemModules,
    bool? IncludeInactiveModules,
    IReadOnlyList<string> AllowMissingPrimaryKey,
    IReadOnlyList<string> AllowMissingSchema);

public sealed record CacheOptionsOverrides(string? Root, bool? Refresh);

public sealed record BuildSsdtOverrides(
    string? ModelPath,
    string? ProfilePath,
    string? OutputDirectory,
    string? ProfilerProvider,
    string? StaticDataPath,
    string? RenameOverrides,
    int? MaxDegreeOfParallelism,
    string? SqlMetadataOutputPath);

public sealed record CaptureProfileOverrides(
    string? ModelPath,
    string? OutputDirectory,
    string? ProfilerProvider,
    string? ProfilePath,
    string? SqlMetadataOutputPath);

public sealed record CompareWithDmmOverrides(
    string? ModelPath,
    string? ProfilePath,
    string? DmmPath,
    string? OutputDirectory,
    int? MaxDegreeOfParallelism);

public sealed record AnalyzeOverrides(
    string? ModelPath,
    string? ProfilePath,
    string? OutputDirectory);

public sealed record ExtractModelOverrides(
    IReadOnlyList<string>? Modules,
    bool? IncludeSystemModules,
    bool? OnlyActiveAttributes,
    string? OutputPath,
    string? MockAdvancedSqlManifest,
    string? SqlMetadataOutputPath);
