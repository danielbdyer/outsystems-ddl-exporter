using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Pipeline.RemapUsers;
using Osm.Pipeline.Sql;

namespace Osm.Cli;

/// <summary>
/// Entry point for the remap-users CLI command.
/// </summary>
public sealed class RemapUsersCommand
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<RemapUsersCommand> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public RemapUsersCommand(ILogger<RemapUsersCommand> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public async Task<int> ExecuteAsync(RemapUsersOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            var runParameters = CreateRunParameters(options);
            var manifestPath = Path.Combine(options.OutputDirectory, "run.manifest.json");
            var hashPath = Path.Combine(options.OutputDirectory, "dry-run.hash");

            var connectionOptions = new SqlConnectionOptions(null, null, "osm-remap-users", null);
            var connectionFactory = new SqlConnectionFactory(options.UatConnectionString, connectionOptions);
            var schemaGraph = new SqlSchemaGraph(connectionFactory);
            var sqlRunner = new SqlRemapUsersRunner(connectionFactory);
            var bulkLoader = new SnapshotBulkLoader(connectionFactory);
            var telemetryLogger = _loggerFactory.CreateLogger<RemapUsersTelemetry>();
            var telemetry = new RemapUsersTelemetry(telemetryLogger, options.LogLevel);
            var artifactWriter = new RemapUsersArtifactWriter(options.OutputDirectory);

            var context = new RemapUsersContext(
                options.SourceEnvironment,
                options.UatConnectionString,
                options.SnapshotPath,
                options.MatchingRules,
                options.FallbackUserId,
                options.Policy,
                options.PolicyWasExplicit,
                options.DryRun,
                options.OutputDirectory,
                options.BatchSize,
                options.CommandTimeoutSeconds,
                options.Parallelism,
                options.UserTable,
                schemaGraph,
                sqlRunner,
                bulkLoader,
                telemetry,
                artifactWriter,
                options.LogLevel,
                options.IncludePii,
                options.RebuildMap);

            if (!options.DryRun)
            {
                var commitReady = await ValidateCommitReadinessAsync(
                    manifestPath,
                    hashPath,
                    runParameters,
                    context.DryRunHash,
                    cancellationToken).ConfigureAwait(false);
                if (!commitReady)
                {
                    _logger.LogError("A matching dry-run from the last 24 hours is required before running with --dry-run false.");
                    return 1;
                }
            }

            var pipeline = new RemapUsersPipeline();
            await pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (options.DryRun)
            {
                var manifest = new RemapUsersRunManifest(runParameters, DateTimeOffset.UtcNow);
                await WriteManifestAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("remap-users pipeline completed successfully (dry-run: {DryRun}).", options.DryRun);
            return 0;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("remap-users pipeline cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "remap-users pipeline failed.");
            return 1;
        }
    }

    private static RemapUsersRunParameters CreateRunParameters(RemapUsersOptions options)
    {
        return new RemapUsersRunParameters(
            options.SourceEnvironment,
            Path.GetFullPath(options.SnapshotPath),
            options.MatchingRules.ToArray(),
            options.Policy,
            options.IncludePii,
            options.RebuildMap,
            options.DryRun,
            options.UserTable,
            options.BatchSize,
            options.CommandTimeoutSeconds,
            options.Parallelism,
            options.FallbackUserId).Normalize();
    }

    private static async Task WriteManifestAsync(string manifestPath, RemapUsersRunManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, manifest, ManifestSerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ValidateCommitReadinessAsync(
        string manifestPath,
        string hashPath,
        RemapUsersRunParameters commitParameters,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("No previous dry-run manifest found at {ManifestPath}.", manifestPath);
            return false;
        }

        try
        {
            await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var manifest = await JsonSerializer.DeserializeAsync<RemapUsersRunManifest>(stream, ManifestSerializerOptions, cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                _logger.LogWarning("Unable to deserialize dry-run manifest at {ManifestPath}.", manifestPath);
                return false;
            }

            var allowed = manifest.MatchesForCommit(commitParameters, DateTimeOffset.UtcNow, TimeSpan.FromHours(24));
            if (!allowed)
            {
                _logger.LogWarning("Existing dry-run manifest does not match current parameters or has expired.");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read previous dry-run manifest at {ManifestPath}.", manifestPath);
            return false;
        }

        if (!File.Exists(hashPath))
        {
            _logger.LogWarning("No dry-run hash found at {HashPath}.", hashPath);
            return false;
        }

        string? observedHash;
        try
        {
            observedHash = (await File.ReadAllTextAsync(hashPath, cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read dry-run hash at {HashPath}.", hashPath);
            return false;
        }

        if (string.IsNullOrWhiteSpace(observedHash))
        {
            _logger.LogWarning("Dry-run hash at {HashPath} is empty.", hashPath);
            return false;
        }

        if (!string.Equals(observedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Dry-run hash mismatch. Expected {ExpectedHash} but found {ObservedHash}.", expectedHash, observedHash);
            return false;
        }

        var hashAge = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(hashPath);
        if (hashAge > TimeSpan.FromHours(24))
        {
            _logger.LogWarning("Dry-run hash at {HashPath} is older than 24 hours.", hashPath);
            return false;
        }

        return true;
    }
}
