using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.App.Configuration;
using Osm.Domain.Abstractions;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Mediation;

namespace Osm.App.UseCases;

public sealed record ExtractModelUseCaseInput(
    CliConfigurationContext ConfigurationContext,
    ExtractModelOverrides Overrides,
    SqlOptionsOverrides Sql);

public sealed record ExtractModelUseCaseResult(
    ModelExtractionResult ExtractionResult,
    string OutputPath);

public sealed class ExtractModelUseCase
{
    private readonly ICommandDispatcher _dispatcher;

    public ExtractModelUseCase(ICommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<Result<ExtractModelUseCaseResult>> RunAsync(ExtractModelUseCaseInput input, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var modules = input.Overrides.Modules ?? Array.Empty<string>();
        var commandResult = ModelExtractionCommand.Create(modules, input.Overrides.IncludeSystemModules, input.Overrides.OnlyActiveAttributes);
        if (commandResult.IsFailure)
        {
            return Result<ExtractModelUseCaseResult>.Failure(commandResult.Errors);
        }

        var outputPath = string.IsNullOrWhiteSpace(input.Overrides.OutputPath)
            ? "model.extracted.json"
            : input.Overrides.OutputPath!;

        var sqlOptionsResult = SqlOptionsResolver.Resolve(input.ConfigurationContext.Configuration, input.Sql);
        if (sqlOptionsResult.IsFailure)
        {
            return Result<ExtractModelUseCaseResult>.Failure(sqlOptionsResult.Errors);
        }

        var request = new ExtractModelPipelineRequest(
            commandResult.Value,
            sqlOptionsResult.Value,
            input.Overrides.MockAdvancedSqlManifest);

        var extractionResult = await _dispatcher.DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<ExtractModelUseCaseResult>.Failure(extractionResult.Errors);
        }

        return new ExtractModelUseCaseResult(extractionResult.Value, outputPath);
    }
}
