using System;
using Osm.Domain.Abstractions;
using Osm.Pipeline.ModelIngestion;

namespace Osm.Pipeline.Orchestration;

internal sealed class ModuleFilterRunner
{
    private readonly ModuleFilter _moduleFilter;

    public ModuleFilterRunner(ModuleFilter moduleFilter)
    {
        _moduleFilter = moduleFilter ?? throw new ArgumentNullException(nameof(moduleFilter));
    }

    public Result<BootstrapPipelineContext> Run(BootstrapPipelineContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Model is null)
        {
            throw new InvalidOperationException("Model must be loaded before applying filters.");
        }

        var filteredResult = _moduleFilter.Apply(context.Model, context.Request.ModuleFilter);
        if (filteredResult.IsFailure)
        {
            return Result<BootstrapPipelineContext>.Failure(filteredResult.Errors);
        }

        context.SetFilteredModel(filteredResult.Value);

        return Result<BootstrapPipelineContext>.Success(context);
    }
}
