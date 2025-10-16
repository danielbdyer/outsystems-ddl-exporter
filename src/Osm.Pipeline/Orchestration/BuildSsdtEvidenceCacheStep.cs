using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtEvidenceCacheStep : IBuildSsdtStep
{
    private readonly IEvidenceCacheService _cacheService;

    public BuildSsdtEvidenceCacheStep(IEvidenceCacheService cacheService)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<Result<BuildSsdtPipelineContext>> ExecuteAsync(
        BuildSsdtPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var request = context.Request;
        EvidenceCacheResult? cacheResult = null;
        if (request.EvidenceCache is { } cacheOptions && !string.IsNullOrWhiteSpace(cacheOptions.RootDirectory))
        {
            var metadata = cacheOptions.Metadata ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            context.Log.Record(
                "evidence.cache.requested",
                "Caching pipeline inputs.",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["rootDirectory"] = cacheOptions.RootDirectory?.Trim(),
                    ["refresh"] = cacheOptions.Refresh ? "true" : "false",
                    ["metadataCount"] = metadata.Count.ToString(CultureInfo.InvariantCulture)
                });

            var cacheRequest = new EvidenceCacheRequest(
                cacheOptions.RootDirectory!.Trim(),
                cacheOptions.Command,
                cacheOptions.ModelPath,
                cacheOptions.ProfilePath,
                cacheOptions.DmmPath,
                cacheOptions.ConfigPath,
                metadata,
                cacheOptions.Refresh);

            var execution = await _cacheService.CacheAsync(cacheRequest, cancellationToken).ConfigureAwait(false);
            if (execution.IsFailure)
            {
                return Result<BuildSsdtPipelineContext>.Failure(execution.Errors);
            }

            cacheResult = execution.Value;
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

            context.Log.Record(cacheEvent, cacheMessage, cacheMetadata);
        }
        else
        {
            context.Log.Record(
                "evidence.cache.skipped",
                "Evidence cache disabled for request.");
        }

        context.SetEvidenceCache(cacheResult);
        return Result<BuildSsdtPipelineContext>.Success(context);
    }
}
