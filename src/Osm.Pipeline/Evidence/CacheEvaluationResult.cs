using System.Collections.Generic;

namespace Osm.Pipeline.Evidence;

internal abstract record CacheEvaluationResult
{
    private CacheEvaluationResult()
    {
    }

    public sealed record Reuse(EvidenceCacheResult Result) : CacheEvaluationResult;

    public sealed record Invalidate(
        EvidenceCacheInvalidationReason Reason,
        IReadOnlyDictionary<string, string?> Metadata) : CacheEvaluationResult;

    public static CacheEvaluationResult CreateReuse(EvidenceCacheResult result) => new Reuse(result);

    public static CacheEvaluationResult CreateInvalidation(
        EvidenceCacheInvalidationReason reason,
        IReadOnlyDictionary<string, string?> metadata) => new Invalidate(reason, metadata);
}
