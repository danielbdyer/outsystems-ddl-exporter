using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtEvidenceCacheStep : IBuildSsdtStep<BuildSsdtState, BuildSsdtState>
{
    private readonly EvidenceCacheCoordinator _cacheCoordinator;

    public BuildSsdtEvidenceCacheStep(EvidenceCacheCoordinator cacheCoordinator)
    {
        _cacheCoordinator = cacheCoordinator ?? throw new ArgumentNullException(nameof(cacheCoordinator));
    }

    public async Task<Result<BuildSsdtState>> ExecuteAsync(
        BuildSsdtState state,
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
            return Result<BuildSsdtState>.Failure(cacheResult.Errors);
        }

        return Result<BuildSsdtState>.Success(state with
        {
            EvidenceCache = cacheResult.Value,
        });
    }
}
