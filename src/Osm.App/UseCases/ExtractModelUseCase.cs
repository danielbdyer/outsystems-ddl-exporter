using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.App.Configuration;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

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
    private readonly IModelJsonDeserializer _deserializer;

    public ExtractModelUseCase(IModelJsonDeserializer deserializer)
    {
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
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

        var executorResult = ResolveExecutor(sqlOptionsResult.Value, input.Overrides.MockAdvancedSqlManifest);
        if (executorResult.IsFailure)
        {
            return Result<ExtractModelUseCaseResult>.Failure(executorResult.Errors);
        }

        var extractionService = new SqlModelExtractionService(executorResult.Value, _deserializer);
        var extractionResult = await extractionService.ExtractAsync(commandResult.Value, cancellationToken).ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<ExtractModelUseCaseResult>.Failure(extractionResult.Errors);
        }

        return new ExtractModelUseCaseResult(extractionResult.Value, outputPath);
    }

    private static Result<IAdvancedSqlExecutor> ResolveExecutor(ResolvedSqlOptions sqlOptions, string? manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            return Result<IAdvancedSqlExecutor>.Success(new FixtureAdvancedSqlExecutor(manifestPath!));
        }

        if (string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            return ValidationError.Create(
                "pipeline.extractModel.sqlConnection.missing",
                "Connection string is required for live extraction. Provide --connection-string or configure sql.connectionString.");
        }

        var samplingOptions = new SqlSamplingOptions(
            sqlOptions.Sampling.RowSamplingThreshold ?? SqlSamplingOptions.Default.RowCountSamplingThreshold,
            sqlOptions.Sampling.SampleSize ?? SqlSamplingOptions.Default.SampleSize);

        var connectionOptions = new SqlConnectionOptions(
            sqlOptions.Authentication.Method,
            sqlOptions.Authentication.TrustServerCertificate,
            sqlOptions.Authentication.ApplicationName,
            sqlOptions.Authentication.AccessToken);

        var executionOptions = new SqlExecutionOptions(sqlOptions.CommandTimeoutSeconds, samplingOptions);
        var executor = new SqlClientAdvancedSqlExecutor(
            new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions),
            new EmbeddedAdvancedSqlScriptProvider(),
            executionOptions);

        return Result<IAdvancedSqlExecutor>.Success(executor);
    }
}
