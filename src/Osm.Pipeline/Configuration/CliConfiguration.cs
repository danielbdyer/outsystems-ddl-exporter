using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Osm.Domain.Configuration;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.UatUsers;
using Osm.Smo;

namespace Osm.Pipeline.Configuration;

public sealed record CliConfiguration(
    TighteningOptions Tightening,
    string? ModelPath,
    string? ProfilePath,
    string? DmmPath,
    CacheConfiguration Cache,
    ProfilerConfiguration Profiler,
    SqlConfiguration Sql,
    ModuleFilterConfiguration ModuleFilter,
    TypeMappingConfiguration TypeMapping,
    SupplementalModelConfiguration SupplementalModels,
    DynamicDataConfiguration DynamicData,
    UatUsersConfiguration UatUsers)
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
        TypeMappingConfiguration.Empty,
        SupplementalModelConfiguration.Empty,
        DynamicDataConfiguration.Empty,
        UatUsersConfiguration.Empty);
}

public sealed record DynamicDataConfiguration(DynamicInsertOutputMode? InsertMode)
{
    public static DynamicDataConfiguration Empty { get; } = new((DynamicInsertOutputMode?)null);
}

public sealed record CacheConfiguration(string? Root, bool? Refresh, int? TimeToLiveSeconds)
{
    public static CacheConfiguration Empty { get; } = new(null, null, null);

    public CacheConfiguration(string? Root, bool? Refresh)
        : this(Root, Refresh, null)
    {
    }
}

public sealed record ProfilerConfiguration(string? Provider, string? ProfilePath, string? MockFolder)
{
    public static ProfilerConfiguration Empty { get; } = new(null, null, null);
}

public sealed record SqlConfiguration(
    string? ConnectionString,
    int? CommandTimeoutSeconds,
    SqlSamplingConfiguration Sampling,
    SqlAuthenticationConfiguration Authentication,
    MetadataContractConfiguration MetadataContract,
    IReadOnlyList<string> ProfilingConnectionStrings,
    IReadOnlyList<TableNameMappingConfiguration> TableNameMappings)
{
    public static SqlConfiguration Empty { get; } = new(
        null,
        null,
        SqlSamplingConfiguration.Empty,
        SqlAuthenticationConfiguration.Empty,
        MetadataContractConfiguration.Empty,
        Array.Empty<string>(),
        Array.Empty<TableNameMappingConfiguration>());
}

public sealed record TableNameMappingConfiguration(
    string SourceSchema,
    string SourceTable,
    string TargetSchema,
    string TargetTable)
{
    public static TableNameMappingConfiguration Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
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

public sealed record MetadataContractConfiguration(
    IReadOnlyDictionary<string, IReadOnlyList<string>> OptionalColumns)
{
    public static MetadataContractConfiguration Empty { get; }
        = new MetadataContractConfiguration(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
}

public sealed record ModuleFilterConfiguration(
    IReadOnlyList<string> Modules,
    bool? IncludeSystemModules,
    bool? IncludeInactiveModules,
    IReadOnlyDictionary<string, IReadOnlyList<string>> EntityFilters,
    IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration> ValidationOverrides)
{
    public static ModuleFilterConfiguration Empty { get; }
        = new ModuleFilterConfiguration(
            Array.Empty<string>(),
            null,
            null,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase));
}

public sealed record TypeMappingConfiguration(
    string? Path,
    TypeMappingRuleDefinition? Default,
    IReadOnlyDictionary<string, TypeMappingRuleDefinition> Overrides)
{
    public static TypeMappingConfiguration Empty { get; }
        = new(null, null, new Dictionary<string, TypeMappingRuleDefinition>(StringComparer.OrdinalIgnoreCase));
}

public sealed record SupplementalModelConfiguration(
    bool? IncludeUsers,
    IReadOnlyList<string> Paths)
{
    public static SupplementalModelConfiguration Empty { get; }
        = new SupplementalModelConfiguration(true, Array.Empty<string>());
}

public sealed record UatUsersConfiguration(
    string? ModelPath,
    bool? FromLiveMetadata,
    string? UserSchema,
    string? UserTable,
    string? UserIdColumn,
    IReadOnlyList<string> IncludeColumns,
    string? OutputRoot,
    string? UserMapPath,
    string? UatUserInventoryPath,
    string? QaUserInventoryPath,
    string? SnapshotPath,
    string? UserEntityIdentifier,
    UserMatchingStrategy? MatchingStrategy,
    string? MatchingAttribute,
    string? MatchingRegexPattern,
    UserFallbackAssignmentMode? FallbackAssignment,
    IReadOnlyList<string> FallbackTargets,
    bool? IdempotentEmission)
{
    public static UatUsersConfiguration Empty { get; }
        = new(
            ModelPath: null,
            FromLiveMetadata: null,
            UserSchema: null,
            UserTable: null,
            UserIdColumn: null,
            IncludeColumns: Array.Empty<string>(),
            OutputRoot: null,
            UserMapPath: null,
            UatUserInventoryPath: null,
            QaUserInventoryPath: null,
            SnapshotPath: null,
            UserEntityIdentifier: null,
            MatchingStrategy: null,
            MatchingAttribute: null,
            MatchingRegexPattern: null,
            FallbackAssignment: null,
            FallbackTargets: Array.Empty<string>(),
            IdempotentEmission: null);
}
