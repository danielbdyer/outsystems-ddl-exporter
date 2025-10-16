using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Orchestration;

public sealed class EvidenceCacheCoordinator
{
    private readonly IEvidenceCacheService _cacheService;

    public EvidenceCacheCoordinator(IEvidenceCacheService cacheService)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<Result<EvidenceCacheResult?>> CacheAsync(
        EvidenceCachePipelineOptions? cacheOptions,
        PipelineExecutionLogBuilder log,
        CancellationToken cancellationToken = default)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (cacheOptions is not { RootDirectory: { } rootDirectory } || string.IsNullOrWhiteSpace(rootDirectory))
        {
            log.Record(
                "evidence.cache.skipped",
                "Evidence cache disabled for request.");

            return Result<EvidenceCacheResult?>.Success(null);
        }

        var trimmedRoot = rootDirectory.Trim();
        var metadata = cacheOptions.Metadata ?? new Dictionary<string, string?>(StringComparer.Ordinal);

        log.Record(
            "evidence.cache.requested",
            "Caching pipeline inputs.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["rootDirectory"] = trimmedRoot,
                ["refresh"] = cacheOptions.Refresh ? "true" : "false",
                ["metadataCount"] = metadata.Count.ToString(CultureInfo.InvariantCulture)
            });

        if (cacheOptions.RetentionMaxAge is { } maxAge)
        {
            log.Record(
                "evidence.cache.retention.maxAge",
                "Applying evidence cache max age policy.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["maxAgeSeconds"] = maxAge.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                });
        }

        if (cacheOptions.RetentionMaxEntries is { } maxEntries)
        {
            log.Record(
                "evidence.cache.retention.maxEntries",
                "Applying evidence cache max entries policy.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["maxEntries"] = maxEntries.ToString(CultureInfo.InvariantCulture)
                });
        }

        var cacheRequest = new EvidenceCacheRequest(
            trimmedRoot,
            cacheOptions.Command,
            cacheOptions.ModelPath,
            cacheOptions.ProfilePath,
            cacheOptions.DmmPath,
            cacheOptions.ConfigPath,
            metadata,
            cacheOptions.Refresh,
            cacheOptions.RetentionMaxAge,
            cacheOptions.RetentionMaxEntries);

        var cacheExecution = await _cacheService.CacheAsync(cacheRequest, cancellationToken).ConfigureAwait(false);
        if (cacheExecution.IsFailure)
        {
            return Result<EvidenceCacheResult?>.Failure(cacheExecution.Errors);
        }

        var cacheResult = cacheExecution.Value;
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

        var cacheEvent = evaluation.Outcome == EvidenceCacheOutcome.Reused
            ? "evidence.cache.reused"
            : "evidence.cache.persisted";
        var cacheMessage = evaluation.Outcome == EvidenceCacheOutcome.Reused
            ? "Reused evidence cache manifest."
            : "Persisted evidence cache manifest.";

        log.Record(cacheEvent, cacheMessage, cacheMetadata);

        return Result<EvidenceCacheResult?>.Success(cacheResult);
    }
}
