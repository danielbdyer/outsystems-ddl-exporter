using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class ApplyMatchingStrategyStep : IPipelineStep<UatUsersContext>
{
    private readonly UserMatchingEngine _engine;
    private readonly ILogger<ApplyMatchingStrategyStep> _logger;

    public ApplyMatchingStrategyStep(
        ILogger<ApplyMatchingStrategyStep>? logger = null,
        UserMatchingEngine? engine = null)
    {
        _engine = engine ?? new UserMatchingEngine();
        _logger = logger ?? NullLogger<ApplyMatchingStrategyStep>.Instance;
    }

    public string Name => "apply-matching-strategy";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.OrphanUserIds.Count == 0)
        {
            context.SetAutomaticMappings(Array.Empty<UserMappingEntry>());
            context.SetMatchingResults(Array.Empty<UserMatchingResult>());
            _logger.LogInformation("No orphans present; skipping matching strategy.");
            return Task.CompletedTask;
        }

        var results = _engine.Execute(context);
        context.SetMatchingResults(results);

        var automatic = results
            .Where(result => result.TargetUserId.HasValue)
            .Select(result => new UserMappingEntry(
                result.SourceUserId,
                result.TargetUserId,
                string.IsNullOrWhiteSpace(result.Explanation) ? "auto-matched" : result.Explanation))
            .ToArray();

        context.SetAutomaticMappings(automatic);
        _logger.LogInformation(
            "Matching strategy proposed {Count} automatic mappings.",
            automatic.Length);
        return Task.CompletedTask;
    }
}
