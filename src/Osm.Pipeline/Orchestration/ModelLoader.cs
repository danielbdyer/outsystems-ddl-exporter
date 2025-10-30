using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.ModelIngestion;

namespace Osm.Pipeline.Orchestration;

internal sealed class ModelLoader
{
    private readonly IModelIngestionService _modelIngestionService;

    public ModelLoader(IModelIngestionService modelIngestionService)
    {
        _modelIngestionService = modelIngestionService ?? throw new ArgumentNullException(nameof(modelIngestionService));
    }

    public async Task<Result<BootstrapPipelineContext>> LoadAsync(
        BootstrapPipelineContext context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Request.InlineModel is { } inlineModel)
        {
            var warnings = context.Request.ModelWarnings;
            if (warnings.IsDefault)
            {
                warnings = ImmutableArray<string>.Empty;
            }

            context.SetModel(inlineModel, warnings);
            return Result<BootstrapPipelineContext>.Success(context);
        }

        var ingestionWarnings = new List<string>();
        var ingestionOptions = new ModelIngestionOptions(context.Request.ModuleFilter.ValidationOverrides, null);

        var modelResult = await _modelIngestionService
            .LoadFromFileAsync(context.Request.ModelPath, ingestionWarnings, cancellationToken, ingestionOptions)
            .ConfigureAwait(false);

        if (modelResult.IsFailure)
        {
            return Result<BootstrapPipelineContext>.Failure(modelResult.Errors);
        }

        context.SetModel(modelResult.Value, ingestionWarnings);

        return Result<BootstrapPipelineContext>.Success(context);
    }
}
