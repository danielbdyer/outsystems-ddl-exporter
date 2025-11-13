using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class LoadQaUserInventoryStep : IPipelineStep<UatUsersContext>
{
    private readonly ILogger<LoadQaUserInventoryStep> _logger;

    public LoadQaUserInventoryStep(ILogger<LoadQaUserInventoryStep>? logger = null)
    {
        _logger = logger ?? NullLogger<LoadQaUserInventoryStep>.Instance;
    }

    public string Name => "load-qa-user-inventory";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation(
            "Loading QA user inventory from {Path}.",
            context.QaUserInventoryPath);

        var result = UserInventoryLoader.Load(context.QaUserInventoryPath);
        context.SetQaUserInventory(result.Records);

        _logger.LogInformation(
            "Loaded {Count} QA user records ({RowCount} rows).",
            result.Records.Count,
            result.RowCount);

        if (result.RowCount == 0)
        {
            _logger.LogWarning("QA user inventory contained zero data rows.");
        }

        return Task.CompletedTask;
    }
}
