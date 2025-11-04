using System;
using System.Collections.Immutable;
using Osm.Domain.Configuration;

namespace Osm.Pipeline.Sql;

/// <summary>
/// Maps a table name from the model (source) to a different name in the target database.
/// Used for handling table name differences between environments (e.g., dev vs QA).
/// </summary>
public sealed record TableNameMapping(
    string SourceSchema,
    string SourceTable,
    string TargetSchema,
    string TargetTable)
{
    public TableNameMapping(string sourceSchema, string sourceTable, string targetSchema, string targetTable)
    {
        if (string.IsNullOrWhiteSpace(sourceSchema))
        {
            throw new ArgumentException("Source schema must be provided.", nameof(sourceSchema));
        }

        if (string.IsNullOrWhiteSpace(sourceTable))
        {
            throw new ArgumentException("Source table must be provided.", nameof(sourceTable));
        }

        if (string.IsNullOrWhiteSpace(targetSchema))
        {
            throw new ArgumentException("Target schema must be provided.", nameof(targetSchema));
        }

        if (string.IsNullOrWhiteSpace(targetTable))
        {
            throw new ArgumentException("Target table must be provided.", nameof(targetTable));
        }

        SourceSchema = sourceSchema.Trim();
        SourceTable = sourceTable.Trim();
        TargetSchema = targetSchema.Trim();
        TargetTable = targetTable.Trim();
    }
}

public sealed record SqlProfilerOptions
{
    public SqlProfilerOptions(
        int? commandTimeoutSeconds,
        SqlSamplingOptions sampling,
        int maxConcurrentTableProfiles,
        SqlProfilerLimits limits,
        NamingOverrideOptions namingOverrides,
        bool allowMissingTables = false,
        ImmutableArray<TableNameMapping> tableNameMappings = default)
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
        AllowMissingTables = allowMissingTables;
        TableNameMappings = tableNameMappings.IsDefault ? ImmutableArray<TableNameMapping>.Empty : tableNameMappings;
    }

    public int? CommandTimeoutSeconds { get; init; }

    public SqlSamplingOptions Sampling { get; init; }

    public int MaxConcurrentTableProfiles { get; init; }

    public SqlProfilerLimits Limits { get; init; }

    public NamingOverrideOptions NamingOverrides { get; init; }

    /// <summary>
    /// When true, profiling gracefully skips missing tables to handle environment drift.
    /// When false (default), profiling fails fast if a table doesn't exist in the database.
    /// Primary environments should use false (strict mode), secondary environments should use true (lenient mode).
    /// </summary>
    public bool AllowMissingTables { get; init; }

    /// <summary>
    /// Maps table names from the model to different names in the target database.
    /// Allows operators to handle scenarios where tables have different names between environments.
    /// Example: dev environment has "dbo.Customer" but QA has "dbo.Customer_V2".
    /// Only used when AllowMissingTables is true (lenient mode for secondary environments).
    /// </summary>
    public ImmutableArray<TableNameMapping> TableNameMappings { get; init; }

    public static SqlProfilerOptions Default { get; } = new(
        null,
        SqlSamplingOptions.Default,
        4,
        SqlProfilerLimits.Default,
        NamingOverrideOptions.Empty,
        allowMissingTables: false,
        tableNameMappings: ImmutableArray<TableNameMapping>.Empty);
}
