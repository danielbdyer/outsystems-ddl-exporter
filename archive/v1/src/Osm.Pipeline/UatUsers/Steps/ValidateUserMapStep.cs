using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class ValidateUserMapStep : IPipelineStep<UatUsersContext>
{
    private readonly ILogger<ValidateUserMapStep> _logger;

    public ValidateUserMapStep(ILogger<ValidateUserMapStep>? logger = null)
    {
        _logger = logger ?? NullLogger<ValidateUserMapStep>.Instance;
    }

    public string Name => "validate-user-map";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var errors = new List<string>();
        var seenSources = new HashSet<UserIdentifier>();
        var missingTargets = new List<UserIdentifier>();

        if (context.QaUserInventory.Count == 0)
        {
            errors.Add("QA user inventory contains zero rows.");
        }

        foreach (var entry in context.UserMap)
        {
            if (!seenSources.Add(entry.SourceUserId))
            {
                errors.Add($"Duplicate SourceUserId '{entry.SourceUserId}' detected in user map.");
            }

            if (!context.TryGetQaUser(entry.SourceUserId, out _))
            {
                errors.Add($"SourceUserId '{entry.SourceUserId}' is not present in the QA inventory.");
            }

            if (!context.IsOrphan(entry.SourceUserId))
            {
                errors.Add($"SourceUserId '{entry.SourceUserId}' is not part of the discovered orphan set.");
            }

            if (entry.TargetUserId is null)
            {
                missingTargets.Add(entry.SourceUserId);
                continue;
            }

            if (!context.IsAllowedUser(entry.TargetUserId.Value))
            {
                errors.Add($"TargetUserId '{entry.TargetUserId}' is not present in the allowed UAT user inventory.");
            }
        }

        var missingSources = context.OrphanUserIds.Where(id => !seenSources.Contains(id)).ToList();
        if (missingSources.Count > 0)
        {
            errors.Add($"Missing mappings for {missingSources.Count} orphan user ids: {FormatPreview(missingSources)}.");
        }

        if (missingTargets.Count > 0)
        {
            errors.Add($"Mappings missing TargetUserId for {missingTargets.Count} source ids: {FormatPreview(missingTargets)}.");
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                _logger.LogError("User map validation error: {Error}", error);
            }

            throw new InvalidOperationException("User map validation failed. Resolve the reported errors and re-run the command.");
        }

        _logger.LogInformation(
            "User map validated successfully. Entries={EntryCount}, Orphans={OrphanCount}, QaUsers={QaCount}, AllowedTargets={AllowedCount}.",
            context.UserMap.Count,
            context.OrphanUserIds.Count,
            context.QaUserInventory.Count,
            context.AllowedUserIds.Count);

        return Task.CompletedTask;
    }

    private static string FormatPreview(IEnumerable<UserIdentifier> ids)
    {
        return string.Join(", ", ids.Take(5).Select(id => id.ToString()));
    }
}
