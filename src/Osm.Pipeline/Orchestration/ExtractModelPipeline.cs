using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Orchestration;

public sealed class ExtractModelPipeline : ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>
{
    private readonly IModelJsonDeserializer _deserializer;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ExtractModelPipeline> _logger;

    public ExtractModelPipeline(IModelJsonDeserializer? deserializer = null, ILoggerFactory? loggerFactory = null)
    {
        _deserializer = deserializer ?? new ModelJsonDeserializer();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ExtractModelPipeline>();
    }

    public async Task<Result<ModelExtractionResult>> HandleAsync(
        ExtractModelPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation(
            "Starting OutSystems model extraction for {ModuleCount} modules (includeSystem: {IncludeSystem}, onlyActive: {OnlyActive}, fixtureProvided: {HasFixture}).",
            request.Command.ModuleNames.Length,
            request.Command.IncludeSystemModules,
            request.Command.OnlyActiveAttributes,
            !string.IsNullOrWhiteSpace(request.AdvancedSqlFixtureManifestPath));

        if (request.Command.ModuleNames.Length > 0)
        {
            _logger.LogDebug(
                "Requested modules: {ModuleList}.",
                string.Join(",", request.Command.ModuleNames.Select(static module => module.Value)));
        }

        var executorResult = ResolveExecutor(request.SqlOptions, request.AdvancedSqlFixtureManifestPath);
        if (executorResult.IsFailure)
        {
            _logger.LogError(
                "Failed to resolve SQL executor: {Errors}.",
                string.Join(", ", executorResult.Errors.Select(static error => error.Code)));
            return Result<ModelExtractionResult>.Failure(executorResult.Errors);
        }

        _logger.LogInformation("SQL executor resolved successfully. Beginning advanced SQL execution.");

        var extractionService = new SqlModelExtractionService(
            executorResult.Value,
            _deserializer,
            _loggerFactory.CreateLogger<SqlModelExtractionService>());

        var extractionResult = await extractionService
            .ExtractAsync(request.Command, cancellationToken)
            .ConfigureAwait(false);

        if (extractionResult.IsFailure)
        {
            _logger.LogError(
                "Model extraction failed: {Errors}.",
                string.Join(", ", extractionResult.Errors.Select(static error => error.Code)));
        }
        else
        {
            _logger.LogInformation(
                "Model extraction completed at {ExtractedAtUtc} with {WarningCount} warning(s).",
                extractionResult.Value.ExtractedAtUtc,
                extractionResult.Value.Warnings.Count);
        }

        return extractionResult;
    }

    private Result<IAdvancedSqlExecutor> ResolveExecutor(ResolvedSqlOptions sqlOptions, string? manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            _logger.LogInformation(
                "Using advanced SQL fixture manifest at {ManifestPath}.",
                manifestPath);

            return Result<IAdvancedSqlExecutor>.Success(
                new FixtureAdvancedSqlExecutor(
                    manifestPath!,
                    logger: _loggerFactory.CreateLogger<FixtureAdvancedSqlExecutor>()));
        }

        if (string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            _logger.LogError("Live extraction requested without a SQL connection string configured.");
            return ValidationError.Create(
                "pipeline.extractModel.sqlConnection.missing",
                "Connection string is required for live extraction. Provide --connection-string or configure sql.connectionString.");
        }

        var samplingOptions = CreateSamplingOptions(sqlOptions.Sampling);
        var connectionOptions = CreateConnectionOptions(sqlOptions.Authentication);
        var executionOptions = new SqlExecutionOptions(sqlOptions.CommandTimeoutSeconds, samplingOptions);

        _logger.LogInformation(
            "Configuring live SQL executor (timeoutSeconds: {TimeoutSeconds}, samplingThreshold: {SamplingThreshold}, sampleSize: {SampleSize}).",
            sqlOptions.CommandTimeoutSeconds,
            samplingOptions.RowCountSamplingThreshold,
            samplingOptions.SampleSize);

        var executor = new SqlClientAdvancedSqlExecutor(
            new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions),
            new EmbeddedAdvancedSqlScriptProvider(),
            executionOptions,
            _loggerFactory.CreateLogger<SqlClientAdvancedSqlExecutor>());

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
