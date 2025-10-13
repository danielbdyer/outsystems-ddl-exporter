using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Osm.Pipeline.RemapUsers;

/// <summary>
/// Immutable configuration and shared services for the remap-users pipeline.
/// </summary>
public sealed record RemapUsersContext
{
    public RemapUsersContext(
        string sourceEnvironment,
        string uatConnectionString,
        string snapshotPath,
        IEnumerable<string> matchingRules,
        long? fallbackUserId,
        RemapUsersPolicy policy,
        bool dryRun,
        string artifactDirectory,
        int batchSize,
        int commandTimeoutSeconds,
        int parallelism,
        string userTable,
        ISchemaGraph schemaGraph,
        ISqlRunner sqlRunner,
        IBulkLoader bulkLoader,
        IRemapUsersTelemetry telemetry,
        IRemapUsersArtifactWriter artifactWriter,
        RemapUsersLogLevel logLevel,
        bool includePii,
        bool rebuildMap,
        RemapUsersRunParameters runParameters,
        bool policyExplicit,
        RemapUsersState? state = null)
    {
        if (string.IsNullOrWhiteSpace(sourceEnvironment))
        {
            throw new ArgumentException("Source environment is required.", nameof(sourceEnvironment));
        }

        if (string.IsNullOrWhiteSpace(uatConnectionString))
        {
            throw new ArgumentException("UAT connection string is required.", nameof(uatConnectionString));
        }

        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            throw new ArgumentException("Snapshot path is required.", nameof(snapshotPath));
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
            throw new ArgumentException("User table must be provided.", nameof(userTable));
        }

        SchemaGraph = schemaGraph ?? throw new ArgumentNullException(nameof(schemaGraph));
        SqlRunner = sqlRunner ?? throw new ArgumentNullException(nameof(sqlRunner));
        BulkLoader = bulkLoader ?? throw new ArgumentNullException(nameof(bulkLoader));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        ArtifactWriter = artifactWriter ?? throw new ArgumentNullException(nameof(artifactWriter));

        SourceEnvironment = sourceEnvironment.Trim();
        UatConnectionString = uatConnectionString.Trim();
        SnapshotPath = Path.GetFullPath(snapshotPath.Trim());
        RunParameters = runParameters ?? throw new ArgumentNullException(nameof(runParameters));
        if (!string.Equals(RunParameters.SourceEnvironment, SourceEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Run parameters source environment does not match context.", nameof(runParameters));
        }

        if (!string.Equals(Path.GetFullPath(RunParameters.SnapshotPath), SnapshotPath, StringComparison.Ordinal))
        {
            throw new ArgumentException("Run parameters snapshot path does not match context.", nameof(runParameters));
        }

        if (!string.Equals(RunParameters.UserTable, userTable, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Run parameters user table does not match context.", nameof(runParameters));
        }

        if (RunParameters.DryRun != dryRun)
        {
            throw new ArgumentException("Run parameters dry-run flag does not match context.", nameof(runParameters));
        }

        if (RunParameters.Policy != Policy || RunParameters.IncludePii != includePii || RunParameters.RebuildMap != rebuildMap)
        {
            throw new ArgumentException("Run parameters do not align with context policy flags.", nameof(runParameters));
        }

        if (RunParameters.BatchSize != batchSize || RunParameters.CommandTimeoutSeconds != commandTimeoutSeconds || RunParameters.Parallelism != parallelism)
        {
            throw new ArgumentException("Run parameters do not align with batching configuration.", nameof(runParameters));
        }

        if (RunParameters.FallbackUserId != fallbackUserId)
        {
            throw new ArgumentException("Run parameters fallback identifier does not match context.", nameof(runParameters));
        }

        if (!RunParameters.MatchingRules.SequenceEqual(matchingRules ?? throw new ArgumentNullException(nameof(matchingRules)), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Run parameters matching rules do not match context.", nameof(runParameters));
        }

        MatchingRules = RemapUsersMatchRuleExtensions.ParseMany(matchingRules);
        if (MatchingRules.Count == 0)
        {
            throw new ArgumentException("At least one matching rule must be supplied.", nameof(matchingRules));
        }

        if (MatchingRules.Contains(RemapUsersMatchRule.Fallback) && !fallbackUserId.HasValue)
        {
            throw new ArgumentException(
                "Fallback user id must be provided when the fallback match rule is enabled.",
                nameof(fallbackUserId));
        }

        FallbackUserId = fallbackUserId;
        Policy = policy;
        DryRun = dryRun;
        ArtifactDirectory = string.IsNullOrWhiteSpace(artifactDirectory)
            ? Path.GetFullPath("./_artifacts/remap-users")
            : Path.GetFullPath(artifactDirectory);
        BatchSize = batchSize;
        CommandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
        Parallelism = parallelism;
        LogLevel = logLevel;
        IncludePii = includePii;
        RebuildMap = rebuildMap;
        PolicyExplicit = policyExplicit;
        var trimmedUserTable = userTable.Trim();
        var schemaSeparatorIndex = trimmedUserTable.IndexOf('.');
        if (schemaSeparatorIndex > 0)
        {
            UserTableSchema = trimmedUserTable[..schemaSeparatorIndex];
            UserTableName = trimmedUserTable[(schemaSeparatorIndex + 1)..];
        }
        else
        {
            UserTableSchema = "dbo";
            UserTableName = trimmedUserTable;
        }

        UserTable = $"{UserTableSchema}.{UserTableName}";
        UserPrimaryKeyColumn = "Id";
        State = state ?? new RemapUsersState();
    }

    public string SourceEnvironment { get; }

    public string UatConnectionString { get; }

    public string SnapshotPath { get; }

    public IReadOnlyList<RemapUsersMatchRule> MatchingRules { get; }

    public long? FallbackUserId { get; }

    public RemapUsersPolicy Policy { get; }

    public bool DryRun { get; }

    public string ArtifactDirectory { get; }

    public int BatchSize { get; }

    public TimeSpan CommandTimeout { get; }

    public int Parallelism { get; }

    public string UserTable { get; }

    public string UserTableSchema { get; }

    public string UserTableName { get; }

    public string UserPrimaryKeyColumn { get; }

    public ISchemaGraph SchemaGraph { get; }

    public ISqlRunner SqlRunner { get; }

    public IBulkLoader BulkLoader { get; }

    public IRemapUsersTelemetry Telemetry { get; }

    public IRemapUsersArtifactWriter ArtifactWriter { get; }

    public RemapUsersLogLevel LogLevel { get; }

    public bool IncludePii { get; }

    public bool RebuildMap { get; }

    public bool PolicyExplicit { get; }

    public RemapUsersRunParameters RunParameters { get; }

    public RemapUsersState State { get; }

    public IReadOnlyDictionary<string, object?> BuildCommonParameters()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SourceEnv"] = SourceEnvironment,
            ["UserTable"] = UserTable,
            ["UserPrimaryKey"] = UserPrimaryKeyColumn,
            ["FallbackUserId"] = FallbackUserId
        };
    }

    public string FormatStepMessage(string messageTemplate, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, messageTemplate, args);
    }

    public string RedactIdentifier(string? value)
    {
        if (IncludePii)
        {
            return value ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return "hash:" + Convert.ToHexString(hash);
    }
}
