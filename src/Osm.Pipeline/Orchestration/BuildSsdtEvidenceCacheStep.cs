using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtEvidenceCacheStep : IBuildSsdtStep
{
    private readonly EvidenceCacheCoordinator _coordinator;

    public BuildSsdtEvidenceCacheStep(EvidenceCacheCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async Task<Result<BuildSsdtPipelineContext>> ExecuteAsync(
        BuildSsdtPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var coordination = await _coordinator
            .CoordinateAsync(context.Request.EvidenceCache, context.Log, cancellationToken)
            .ConfigureAwait(false);

        if (coordination.IsFailure)
        {
            return Result<BuildSsdtPipelineContext>.Failure(coordination.Errors);
        }

        context.SetEvidenceCache(coordination.Value);
        return Result<BuildSsdtPipelineContext>.Success(context);
    }
}
