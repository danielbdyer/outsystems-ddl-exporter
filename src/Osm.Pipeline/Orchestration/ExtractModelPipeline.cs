using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Orchestration;

public interface IExtractModelPipeline
{
    Task<Result<ModelExtractionResult>> ExecuteAsync(
        ExtractModelPipelineRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ExtractModelPipeline : IExtractModelPipeline
{
    private readonly IModelJsonDeserializer _deserializer;

    public ExtractModelPipeline(IModelJsonDeserializer? deserializer = null)
    {
        _deserializer = deserializer ?? new ModelJsonDeserializer();
    }

    public async Task<Result<ModelExtractionResult>> ExecuteAsync(
        ExtractModelPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var executorResult = ResolveExecutor(request.SqlOptions, request.AdvancedSqlFixtureManifestPath);
        if (executorResult.IsFailure)
        {
            return Result<ModelExtractionResult>.Failure(executorResult.Errors);
        }

        var extractionService = new SqlModelExtractionService(executorResult.Value, _deserializer);
        return await extractionService.ExtractAsync(request.Command, cancellationToken).ConfigureAwait(false);
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

        var samplingOptions = CreateSamplingOptions(sqlOptions.Sampling);
        var connectionOptions = CreateConnectionOptions(sqlOptions.Authentication);
        var executionOptions = new SqlExecutionOptions(sqlOptions.CommandTimeoutSeconds, samplingOptions);
        var executor = new SqlClientAdvancedSqlExecutor(
            new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions),
            new EmbeddedAdvancedSqlScriptProvider(),
            executionOptions);

        return Result<IAdvancedSqlExecutor>.Success(executor);
    }

    private static SqlSamplingOptions CreateSamplingOptions(SqlSamplingSettings settings)
    {
        var threshold = settings.RowSamplingThreshold ?? SqlSamplingOptions.Default.RowCountSamplingThreshold;
        var sampleSize = settings.SampleSize ?? SqlSamplingOptions.Default.SampleSize;
        return new SqlSamplingOptions(threshold, sampleSize);
    }

    private static SqlConnectionOptions CreateConnectionOptions(SqlAuthenticationSettings settings)
    {
        return new SqlConnectionOptions(
            settings.Method,
            settings.TrustServerCertificate,
            settings.ApplicationName,
            settings.AccessToken);
    }
}
