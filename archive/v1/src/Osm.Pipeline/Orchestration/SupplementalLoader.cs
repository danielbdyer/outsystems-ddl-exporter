using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Orchestration;

internal sealed class SupplementalLoader
{
    private readonly SupplementalEntityLoader _loader;

    public SupplementalLoader(SupplementalEntityLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public async Task<Result<BootstrapPipelineContext>> LoadAsync(
        BootstrapPipelineContext context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var supplementalResult = await _loader
            .LoadAsync(context.Request.SupplementalModels, cancellationToken)
            .ConfigureAwait(false);

        if (supplementalResult.IsFailure)
        {
            return Result<BootstrapPipelineContext>.Failure(supplementalResult.Errors);
        }

        context.SetSupplementalEntities(supplementalResult.Value);

        return Result<BootstrapPipelineContext>.Success(context);
    }
}
