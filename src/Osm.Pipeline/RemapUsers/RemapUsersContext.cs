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
        bool policyWasExplicit,
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
        MatchingRules = RemapUsersMatchRuleExtensions.ParseMany(matchingRules ?? throw new ArgumentNullException(nameof(matchingRules)));
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
        PolicyWasExplicit = policyWasExplicit;
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
        DryRunHash = ComputeDryRunHash();
    }

    public string SourceEnvironment { get; }

    public string UatConnectionString { get; }

    public string SnapshotPath { get; }

    public IReadOnlyList<RemapUsersMatchRule> MatchingRules { get; }

    public long? FallbackUserId { get; }

    public RemapUsersPolicy Policy { get; }

    public bool PolicyWasExplicit { get; }

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

    public RemapUsersState State { get; }

    public string DryRunHash { get; }

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

    private string ComputeDryRunHash()
    {
        var builder = new StringBuilder();
        builder.AppendLine(SourceEnvironment);
        builder.AppendLine(SnapshotPath);
        builder.AppendLine(UserTable);
        builder.AppendLine(Policy.ToString());
        builder.AppendLine(PolicyWasExplicit.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(DryRun.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(IncludePii.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(RebuildMap.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(BatchSize.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(((int)CommandTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(Parallelism.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(FallbackUserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        foreach (var rule in MatchingRules)
        {
            builder.AppendLine(rule.ToString());
        }

        foreach (var fingerprint in EnumerateSnapshotFingerprint())
        {
            builder.AppendLine(fingerprint);
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private IEnumerable<string> EnumerateSnapshotFingerprint()
    {
        if (Directory.Exists(SnapshotPath))
        {
            var root = Path.GetFullPath(SnapshotPath);
            foreach (var file in Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string? error = null;
                FileInfo? info = null;
                try
                {
                    info = new FileInfo(file);
                }
                catch (Exception ex)
                {
                    error = "error:" + ex.GetType().Name + ":" + ex.Message;
                }

                if (error is not null)
                {
                    yield return error;
                    continue;
                }

                var relative = Path.GetRelativePath(root, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                yield return string.Join(
                    '|',
                    relative,
                    info!.Length.ToString(CultureInfo.InvariantCulture),
                    info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            }

            yield break;
        }

        if (File.Exists(SnapshotPath))
        {
            string? error = null;
            FileInfo? info = null;
            try
            {
                info = new FileInfo(SnapshotPath);
            }
            catch (Exception ex)
            {
                error = "error:" + ex.GetType().Name + ":" + ex.Message;
            }

            if (error is not null)
            {
                yield return error;
                yield break;
            }

            yield return string.Join(
                '|',
                Path.GetFileName(SnapshotPath),
                info!.Length.ToString(CultureInfo.InvariantCulture),
                info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            yield break;
        }

        yield return "missing:" + SnapshotPath;
    }
}
