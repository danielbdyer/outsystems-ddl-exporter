using System;
using System.Collections.Generic;
using Osm.Pipeline.RemapUsers;

namespace Osm.Cli;

/// <summary>
/// Strongly typed options parsed from the remap-users CLI command.
/// </summary>
public sealed class RemapUsersOptions
{
    public RemapUsersOptions(
        string sourceEnvironment,
        string uatConnectionString,
        string snapshotPath,
        IReadOnlyList<string> matchingRules,
        long? fallbackUserId,
        RemapUsersPolicy policy,
        bool dryRun,
        string outputDirectory,
        int batchSize,
        int commandTimeoutSeconds,
        int parallelism,
        RemapUsersLogLevel logLevel,
        string userTable,
        bool includePii,
        bool rebuildMap)
    {
        SourceEnvironment = !string.IsNullOrWhiteSpace(sourceEnvironment)
            ? sourceEnvironment
            : throw new ArgumentException("Source environment is required.", nameof(sourceEnvironment));
        UatConnectionString = !string.IsNullOrWhiteSpace(uatConnectionString)
            ? uatConnectionString
            : throw new ArgumentException("UAT connection string is required.", nameof(uatConnectionString));
        SnapshotPath = !string.IsNullOrWhiteSpace(snapshotPath)
            ? snapshotPath
            : throw new ArgumentException("Snapshot path is required.", nameof(snapshotPath));
        MatchingRules = matchingRules ?? throw new ArgumentNullException(nameof(matchingRules));
        if (MatchingRules.Count == 0)
        {
            throw new ArgumentException("At least one matching rule must be provided.", nameof(matchingRules));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be greater than zero.");
        }

        if (commandTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds), commandTimeoutSeconds, "Command timeout must be greater than zero.");
        }

        if (parallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parallelism), parallelism, "Parallelism must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(userTable))
        {
            throw new ArgumentException("User table name must be provided.", nameof(userTable));
        }

        FallbackUserId = fallbackUserId;
        Policy = policy;
        DryRun = dryRun;
        OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? "./_artifacts/remap-users" : outputDirectory;
        BatchSize = batchSize;
        CommandTimeoutSeconds = commandTimeoutSeconds;
        Parallelism = parallelism;
        LogLevel = logLevel;
        UserTable = userTable;
        IncludePii = includePii;
        RebuildMap = rebuildMap;
    }

    public string SourceEnvironment { get; }

    public string UatConnectionString { get; }

    public string SnapshotPath { get; }

    public IReadOnlyList<string> MatchingRules { get; }

    public long? FallbackUserId { get; }

    public RemapUsersPolicy Policy { get; }

    public bool DryRun { get; }

    public string OutputDirectory { get; }

    public int BatchSize { get; }

    public int CommandTimeoutSeconds { get; }

    public int Parallelism { get; }

    public RemapUsersLogLevel LogLevel { get; }

    public string UserTable { get; }

    public bool IncludePii { get; }

    public bool RebuildMap { get; }
}
