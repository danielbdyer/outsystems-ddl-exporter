using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtEvidenceCacheStep : IBuildSsdtStep
{
    private readonly EvidenceCacheCoordinator _cacheCoordinator;

    public BuildSsdtEvidenceCacheStep(IEvidenceCacheService cacheService)
    {
        if (cacheService is null)
        {
            throw new ArgumentNullException(nameof(cacheService));
        }

        _cacheCoordinator = new EvidenceCacheCoordinator(cacheService);
    }

    public async Task<Result<BuildSsdtPipelineContext>> ExecuteAsync(
        BuildSsdtPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var cacheResult = await _cacheCoordinator
            .CacheAsync(context.Request.EvidenceCache, context.Log, cancellationToken)
            .ConfigureAwait(false);
        if (cacheResult.IsFailure)
        {
            return Result<BuildSsdtPipelineContext>.Failure(cacheResult.Errors);
        }

        context.SetEvidenceCache(cacheResult.Value);
        return Result<BuildSsdtPipelineContext>.Success(context);
    }
}
