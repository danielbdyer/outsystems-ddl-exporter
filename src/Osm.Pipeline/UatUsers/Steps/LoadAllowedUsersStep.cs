using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class LoadAllowedUsersStep : IPipelineStep<UatUsersContext>
{
    private readonly ILogger<LoadAllowedUsersStep> _logger;

    public LoadAllowedUsersStep(ILogger<LoadAllowedUsersStep>? logger = null)
    {
        _logger = logger ?? NullLogger<LoadAllowedUsersStep>.Instance;
    }

    public string Name => "load-allowed-users";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation(
            "Loading allowed user identifiers. SqlPath={SqlPath}, ListPath={ListPath}.",
            context.AllowedUsersSqlPath ?? "<none>",
            context.AllowedUserIdsPath ?? "<none>");

        var loadResult = AllowedUserLoader.Load(
            context.AllowedUsersSqlPath,
            context.AllowedUserIdsPath,
            context.UserIdColumn);

        context.SetAllowedUserIds(loadResult.UserIds);

        _logger.LogInformation(
            "Loaded {DistinctCount} unique allowed user IDs ({SqlCount} from SQL, {ListCount} from list inputs).",
            context.AllowedUserIds.Count,
            loadResult.SqlRowCount,
            loadResult.ListRowCount);

        return Task.CompletedTask;
    }
}
