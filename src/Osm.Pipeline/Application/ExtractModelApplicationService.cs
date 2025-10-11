using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Application;

public sealed record ExtractModelApplicationInput(
    CliConfigurationContext ConfigurationContext,
    ExtractModelOverrides Overrides,
    SqlOptionsOverrides Sql);

public sealed record ExtractModelApplicationResult(
    ModelExtractionResult ExtractionResult,
    string OutputPath);

public sealed class ExtractModelApplicationService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;

    public ExtractModelApplicationService(ICommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
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

        var extractionResult = await _dispatcher.DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<ExtractModelApplicationResult>.Failure(extractionResult.Errors);
        }

        return new ExtractModelApplicationResult(extractionResult.Value, outputPath);
    }
}
