using System;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class LoadAllowedUsersStep : IPipelineStep<UatUsersContext>
{
    public string Name => "load-allowed-users";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var allowed = AllowedUserLoader.Load(
            context.AllowedUsersSqlPath,
            context.AllowedUserIdsPath,
            context.UserIdColumn);
        context.SetAllowedUserIds(allowed);
        return Task.CompletedTask;
    }
}
