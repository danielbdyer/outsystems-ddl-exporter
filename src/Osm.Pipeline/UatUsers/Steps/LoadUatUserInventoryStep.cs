using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class LoadUatUserInventoryStep : IPipelineStep<UatUsersContext>
{
    private readonly ILogger<LoadUatUserInventoryStep> _logger;

    public LoadUatUserInventoryStep(ILogger<LoadUatUserInventoryStep>? logger = null)
    {
        _logger = logger ?? NullLogger<LoadUatUserInventoryStep>.Instance;
    }

    public string Name => "load-uat-user-inventory";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation(
            "Loading UAT user inventory from {Path}.",
            context.UatUserInventoryPath);

        var result = UserInventoryLoader.Load(context.UatUserInventoryPath);
        context.SetUatUserInventory(result.Records);
        context.SetAllowedUserIds(result.Records.Keys.ToArray());

        _logger.LogInformation(
            "Loaded {Count} allowed UAT user identifiers ({RowCount} rows).",
            context.AllowedUserIds.Count,
            result.RowCount);

        if (context.AllowedUserIds.Count == 0)
        {
            _logger.LogError("UAT user inventory did not contain any identifiers.");
            throw new InvalidOperationException(
                "UAT user inventory did not contain any identifiers. Provide a CSV export of ossys_User from the UAT environment.");
        }

        return Task.CompletedTask;
    }
}
