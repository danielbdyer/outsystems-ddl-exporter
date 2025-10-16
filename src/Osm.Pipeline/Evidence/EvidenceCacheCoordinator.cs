using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Evidence;

public sealed class EvidenceCacheCoordinator
{
    private static readonly EvidenceCacheCoordinatorMessages DefaultMessages = EvidenceCacheCoordinatorMessages.Default;

    private readonly IEvidenceCacheService _cacheService;

    public EvidenceCacheCoordinator(IEvidenceCacheService cacheService)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<Result<EvidenceCacheResult?>> CoordinateAsync(
        EvidenceCachePipelineOptions? options,
        PipelineExecutionLogBuilder log,
        CancellationToken cancellationToken = default,
        EvidenceCacheCoordinatorMessages? messages = null)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        var resolvedMessages = messages ?? DefaultMessages;

        if (options is null || string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            log.Record(resolvedMessages.SkippedEvent, resolvedMessages.SkippedMessage);
            return Result<EvidenceCacheResult?>.Success(null);
        }

        var metadata = options.Metadata ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        var trimmedRoot = options.RootDirectory!.Trim();

        log.Record(
            resolvedMessages.RequestedEvent,
            resolvedMessages.RequestedMessage,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["rootDirectory"] = trimmedRoot,
                ["refresh"] = options.Refresh ? "true" : "false",
                ["metadataCount"] = metadata.Count.ToString(CultureInfo.InvariantCulture)
            });

        var cacheRequest = new EvidenceCacheRequest(
            trimmedRoot,
            options.Command,
            options.ModelPath,
            options.ProfilePath,
            options.DmmPath,
            options.ConfigPath,
            metadata,
            options.Refresh);

        var execution = await _cacheService
            .CacheAsync(cacheRequest, cancellationToken)
            .ConfigureAwait(false);

        if (execution.IsFailure)
        {
            return Result<EvidenceCacheResult?>.Failure(execution.Errors);
        }

        var cacheResult = execution.Value;
        var evaluation = cacheResult.Evaluation;
        var cacheMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["cacheDirectory"] = cacheResult.CacheDirectory,
            ["artifactCount"] = cacheResult.Manifest.Artifacts.Count.ToString(CultureInfo.InvariantCulture),
            ["cacheKey"] = cacheResult.Manifest.Key,
            ["cacheOutcome"] = evaluation.Outcome.ToString(),
            ["cacheReason"] = evaluation.Reason.ToString(),
        };

        foreach (var pair in evaluation.Metadata)
        {
            cacheMetadata[pair.Key] = pair.Value;
        }

        var completionEvent = evaluation.Outcome == EvidenceCacheOutcome.Reused
            ? resolvedMessages.ReusedEvent
            : resolvedMessages.PersistedEvent;
        var completionMessage = evaluation.Outcome == EvidenceCacheOutcome.Reused
            ? resolvedMessages.ReusedMessage
            : resolvedMessages.PersistedMessage;

        log.Record(completionEvent, completionMessage, cacheMetadata);

        return Result<EvidenceCacheResult?>.Success(cacheResult);
    }
}

public sealed record EvidenceCacheCoordinatorMessages(
    string RequestedEvent,
    string RequestedMessage,
    string PersistedEvent,
    string PersistedMessage,
    string ReusedEvent,
    string ReusedMessage,
    string SkippedEvent,
    string SkippedMessage)
{
    public static EvidenceCacheCoordinatorMessages Default { get; } = new(
        "evidence.cache.requested",
        "Caching pipeline inputs.",
        "evidence.cache.persisted",
        "Persisted evidence cache manifest.",
        "evidence.cache.reused",
        "Reused evidence cache manifest.",
        "evidence.cache.skipped",
        "Evidence cache disabled for request.");
}
