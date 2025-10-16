using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Pipeline.ModelIngestion;

namespace Osm.Pipeline.Hosting.Verbs;

public sealed record InspectModelVerbOptions(string ModelPath);

public sealed class InspectModelVerb : PipelineVerb<InspectModelVerbOptions>
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ILogger<InspectModelVerb> _logger;
    private readonly TimeProvider _timeProvider;

    public InspectModelVerb(IModelIngestionService modelIngestionService, ILogger<InspectModelVerb> logger, TimeProvider timeProvider)
    {
        _modelIngestionService = modelIngestionService ?? throw new ArgumentNullException(nameof(modelIngestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public override string Name => "inspect";

    protected override async Task<PipelineVerbResult> RunInternalAsync(InspectModelVerbOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var startedAt = _timeProvider.GetUtcNow();

        try
        {
            var warnings = new List<string>();
            var modelResult = await _modelIngestionService
                .LoadFromFileAsync(options.ModelPath, warnings, cancellationToken)
                .ConfigureAwait(false);
            if (modelResult.IsFailure)
            {
                VerbLogging.LogErrors(_logger, modelResult.Errors);
                return new PipelineVerbResult(1);
            }

            var immutableWarnings = warnings.ToImmutableArray();
            VerbLogging.LogWarnings(_logger, immutableWarnings);

            var model = modelResult.Value;
            var entityCount = model.Modules.Sum(static module => module.Entities.Length);
            var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(static entity => entity.Attributes.Length));

            _logger.LogInformation("Model exported at {Timestamp}.", model.ExportedAtUtc.ToString("O"));
            _logger.LogInformation("Modules: {Count}.", model.Modules.Length);
            _logger.LogInformation("Entities: {Count}.", entityCount);
            _logger.LogInformation("Attributes: {Count}.", attributeCount);

            var completedAt = _timeProvider.GetUtcNow();
            var run = new PipelineRun(Name, startedAt, completedAt, Array.Empty<ArtifactRef>());
            return new PipelineVerbResult(0, run);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("inspect cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "inspect failed.");
            return new PipelineVerbResult(1);
        }
    }
}
