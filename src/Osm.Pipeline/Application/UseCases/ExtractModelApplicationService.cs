using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Application.Configuration;
using Osm.Domain.Abstractions;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Application;

public sealed record ExtractModelApplicationRequest(
    CliConfigurationContext ConfigurationContext,
    ExtractModelOverrides Overrides,
    SqlOptionsOverrides Sql);

public sealed record ExtractModelApplicationResult(
    ModelExtractionResult ExtractionResult,
    string OutputPath);

public interface IExtractModelApplicationService
{
    Task<Result<ExtractModelApplicationResult>> ExecuteAsync(
        ExtractModelApplicationRequest input,
        CancellationToken cancellationToken = default);
}

public sealed class ExtractModelApplicationService : IExtractModelApplicationService
{
    private readonly IExtractModelPipeline _pipeline;

    public ExtractModelApplicationService(IExtractModelPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public async Task<Result<ExtractModelApplicationResult>> ExecuteAsync(ExtractModelApplicationRequest input, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var modules = input.Overrides.Modules ?? Array.Empty<string>();
        var commandResult = ModelExtractionCommand.Create(modules, input.Overrides.IncludeSystemModules, input.Overrides.OnlyActiveAttributes);
        if (commandResult.IsFailure)
        {
            return Result<ExtractModelApplicationResult>.Failure(commandResult.Errors);
        }

        var outputPath = string.IsNullOrWhiteSpace(input.Overrides.OutputPath)
            ? "model.extracted.json"
            : input.Overrides.OutputPath!;

        var sqlOptionsResult = SqlOptionsResolver.Resolve(input.ConfigurationContext.Configuration, input.Sql);
        if (sqlOptionsResult.IsFailure)
        {
            return Result<ExtractModelApplicationResult>.Failure(sqlOptionsResult.Errors);
        }

        var request = new ExtractModelPipelineRequest(
            commandResult.Value,
            sqlOptionsResult.Value,
            input.Overrides.MockAdvancedSqlManifest);

        var extractionResult = await _pipeline.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<ExtractModelApplicationResult>.Failure(extractionResult.Errors);
        }

        return new ExtractModelApplicationResult(extractionResult.Value, outputPath);
    }
}
