using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Hosting.Verbs;

public sealed record ExtractModelVerbOptions(
    string? ConfigPath,
    ExtractModelOverrides Overrides,
    SqlOptionsOverrides Sql);

public sealed class ExtractModelVerb : PipelineVerb<ExtractModelVerbOptions>
{
    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> _applicationService;
    private readonly ILogger<ExtractModelVerb> _logger;
    private readonly TimeProvider _timeProvider;

    public ExtractModelVerb(
        ICliConfigurationService configurationService,
        IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> applicationService,
        ILogger<ExtractModelVerb> logger,
        TimeProvider timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public override string Name => "extract-model";

    protected override async Task<PipelineVerbResult> RunInternalAsync(ExtractModelVerbOptions options, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();

        try
        {
            var configurationResult = await _configurationService
                .LoadAsync(options.ConfigPath, cancellationToken)
                .ConfigureAwait(false);
            if (configurationResult.IsFailure)
            {
                VerbLogging.LogErrors(_logger, configurationResult.Errors);
                return new PipelineVerbResult(1);
            }

            var input = new ExtractModelApplicationInput(
                configurationResult.Value,
                options.Overrides,
                options.Sql);

            var result = await _applicationService
                .RunAsync(input, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                VerbLogging.LogErrors(_logger, result.Errors);
                return new PipelineVerbResult(1);
            }

            return await EmitAsync(result.Value, startedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("extract-model cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "extract-model failed.");
            return new PipelineVerbResult(1);
        }
    }

    private async Task<PipelineVerbResult> EmitAsync(ExtractModelApplicationResult result, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var extraction = result.ExtractionResult;
        var outputPath = result.OutputPath ?? "model.extracted.json";

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
        await using (var outputStream = File.Create(outputPath))
        {
            await extraction.JsonPayload.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
        }

        if (extraction.Warnings.Count > 0)
        {
            foreach (var warning in extraction.Warnings.Where(static w => !string.IsNullOrWhiteSpace(w)))
            {
                _logger.LogWarning("{Warning}", warning);
            }
        }

        var moduleCount = extraction.Model.Modules.Length;
        var entityCount = extraction.Model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = extraction.Model.Modules.Sum(static module => module.Entities.Sum(static entity => entity.Attributes.Length));

        _logger.LogInformation("Extracted {ModuleCount} modules spanning {EntityCount} entities.", moduleCount, entityCount);
        _logger.LogInformation("Attributes: {AttributeCount}.", attributeCount);
        _logger.LogInformation("Model written to {OutputPath}.", outputPath);
        _logger.LogInformation("Extraction timestamp (UTC): {Timestamp}.", extraction.ExtractedAtUtc.ToString("O"));

        var artifacts = new List<ArtifactRef>
        {
            new("model-json", Path.GetFullPath(outputPath))
        };

        var completedAt = _timeProvider.GetUtcNow();
        var run = new PipelineRun(Name, startedAt, completedAt, artifacts);
        return new PipelineVerbResult(0, run);
    }
}
