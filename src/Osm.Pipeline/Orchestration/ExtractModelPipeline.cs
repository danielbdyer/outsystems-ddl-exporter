using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

    public ExtractModelPipeline(IModelJsonDeserializer deserializer, ILoggerFactory loggerFactory)
    {
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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

        if (!string.IsNullOrWhiteSpace(request.SqlMetadataOutputPath))
        {
            _logger.LogInformation(
                "SQL metadata diagnostics will be written to {MetadataPath}.",
                Path.GetFullPath(request.SqlMetadataOutputPath!));
        }

        var readerResult = ResolveMetadataReader(request.SqlOptions, request.AdvancedSqlFixtureManifestPath);
        if (readerResult.IsFailure)
        {
            _logger.LogError(
                "Failed to resolve metadata reader: {Errors}.",
                string.Join(", ", readerResult.Errors.Select(static error => error.Code)));
            request.SqlMetadataLog?.RecordFailure(readerResult.Errors, rowSnapshot: null);
            return Result<ModelExtractionResult>.Failure(readerResult.Errors);
        }

        _logger.LogInformation("Metadata reader resolved successfully. Beginning snapshot execution.");

        var metadataOrchestrator = new AdvancedSqlMetadataOrchestrator(
            readerResult.Value,
            _loggerFactory.CreateLogger<AdvancedSqlMetadataOrchestrator>());
        var snapshotJsonBuilder = new SnapshotJsonBuilder();
        var snapshotValidator = new SnapshotValidator();
        var modelDeserializerFacade = new ModelDeserializerFacade(
            _deserializer,
            _loggerFactory.CreateLogger<ModelDeserializerFacade>());

        var extractionService = new SqlModelExtractionService(
            metadataOrchestrator,
            snapshotJsonBuilder,
            snapshotValidator,
            modelDeserializerFacade,
            _loggerFactory.CreateLogger<SqlModelExtractionService>());

        var extractionOptions = string.IsNullOrWhiteSpace(request.OutputPath)
            ? ModelExtractionOptions.InMemory(request.SqlMetadataOutputPath, request.SqlMetadataLog)
            : ModelExtractionOptions.ToFile(request.OutputPath!, request.SqlMetadataOutputPath, request.SqlMetadataLog);

        var extractionResult = await extractionService
            .ExtractAsync(
                request.Command,
                extractionOptions,
                cancellationToken)
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

    private Result<IOutsystemsMetadataReader> ResolveMetadataReader(ResolvedSqlOptions sqlOptions, string? manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            _logger.LogInformation(
                "Using metadata fixture manifest at {ManifestPath}.",
                manifestPath);

            return Result<IOutsystemsMetadataReader>.Success(
                new FixtureOutsystemsMetadataReader(
                    manifestPath!,
                    logger: _loggerFactory.CreateLogger<FixtureOutsystemsMetadataReader>()));
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
            "Configuring live metadata reader (timeoutSeconds: {TimeoutSeconds}, samplingThreshold: {SamplingThreshold}, sampleSize: {SampleSize}).",
            sqlOptions.CommandTimeoutSeconds,
            samplingOptions.RowCountSamplingThreshold,
            samplingOptions.SampleSize);

        var reader = new SqlClientOutsystemsMetadataReader(
            new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions),
            new EmbeddedOutsystemsMetadataScriptProvider(),
            executionOptions,
            _loggerFactory.CreateLogger<SqlClientOutsystemsMetadataReader>(),
            commandExecutor: null,
            contractOverrides: sqlOptions.MetadataContract,
            loggerFactory: _loggerFactory);

        return Result<IOutsystemsMetadataReader>.Success(reader);
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
