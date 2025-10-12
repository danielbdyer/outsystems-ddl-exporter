using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Osm.Cli;

/// <summary>
/// Entry point for the remap-users CLI command. The implementation currently logs invocation
/// until the underlying pipeline orchestration is available.
/// </summary>
public sealed class RemapUsersCommand
{
    private readonly ILogger<RemapUsersCommand> _logger;

    public RemapUsersCommand(ILogger<RemapUsersCommand> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<int> ExecuteAsync(RemapUsersOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "RemapUsers command invoked for {SourceEnv} snapshot {SnapshotPath} (dry-run: {DryRun}).",
            options.SourceEnvironment,
            options.SnapshotPath,
            options.DryRun);

        _logger.LogWarning(
            "Remap users pipeline orchestration is not yet implemented. Command execution completes without data operations.");

        return Task.FromResult(0);
    }
}
