using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtEvidenceCacheStep : IBuildSsdtStep<BootstrapCompleted, EvidencePrepared>
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

    public async Task<Result<EvidencePrepared>> ExecuteAsync(
        BootstrapCompleted state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var cacheResult = await _cacheCoordinator
            .CacheAsync(state.Request.EvidenceCache, state.Log, cancellationToken)
            .ConfigureAwait(false);
        if (cacheResult.IsFailure)
        {
            return Result<EvidencePrepared>.Failure(cacheResult.Errors);
        }

        return Result<EvidencePrepared>.Success(new EvidencePrepared(
            state.Request,
            state.Log,
            state.Bootstrap,
            cacheResult.Value));
    }
}
