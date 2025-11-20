using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.UatUsers;

public interface IUatUsersPipelineRunner
{
    Task<Result<UatUsersApplicationResult>> RunAsync(
        UatUsersPipelineRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UatUsersPipelineRunner : IUatUsersPipelineRunner
{
    private readonly ILogger<UatUsersPipelineRunner> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public UatUsersPipelineRunner(ILogger<UatUsersPipelineRunner> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public async Task<Result<UatUsersApplicationResult>> RunAsync(
        UatUsersPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Overrides is null)
        {
            throw new ArgumentException("UAT users overrides must be provided.", nameof(request));
        }

        if (!request.Overrides.Enabled)
        {
            _logger.LogDebug("uat-users pipeline disabled; skipping execution.");
            return Result<UatUsersApplicationResult>.Success(UatUsersApplicationResult.Disabled);
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return Result<UatUsersApplicationResult>.Failure(ValidationError.Create(
                "pipeline.fullExport.uatUsers.outputDirectory.missing",
                "Build output directory is required to emit uat-users artifacts."));
        }

        if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
        {
            return Result<UatUsersApplicationResult>.Failure(ValidationError.Create(
                "pipeline.fullExport.uatUsers.connectionString.missing",
                "A source connection string must be supplied when uat-users is enabled."));
        }

        if (string.IsNullOrWhiteSpace(request.Overrides.QaUserInventoryPath))
        {
            return Result<UatUsersApplicationResult>.Failure(ValidationError.Create(
                "pipeline.fullExport.uatUsers.qaInventory.missing",
                "Provide a QA user inventory CSV via --qa-user-inventory."));
        }

        if (string.IsNullOrWhiteSpace(request.Overrides.UatUserInventoryPath))
        {
            return Result<UatUsersApplicationResult>.Failure(ValidationError.Create(
                "pipeline.fullExport.uatUsers.uatInventory.missing",
                "Provide a UAT user inventory CSV via --uat-user-inventory."));
        }

        try
        {
            var artifacts = new UatUsersArtifacts(request.OutputDirectory, request.Overrides.IdempotentEmission);
            var userMapPath = string.IsNullOrWhiteSpace(request.Overrides.UserMapPath)
                ? artifacts.GetDefaultUserMapPath()
                : request.Overrides.UserMapPath!;

            var connectionString = request.SourceConnectionString!.Trim();
            var sqlOptions = new SqlConnectionOptions(
                AuthenticationMethod: null,
                TrustServerCertificate: null,
                ApplicationName: "osm-uat-users",
                AccessToken: null);
            var connectionFactory = new SqlConnectionFactory(connectionString, sqlOptions);

            var schema = string.IsNullOrWhiteSpace(request.Overrides.UserSchema)
                ? "dbo"
                : request.Overrides.UserSchema!;
            var table = string.IsNullOrWhiteSpace(request.Overrides.UserTable)
                ? "User"
                : request.Overrides.UserTable!;
            var userIdColumn = string.IsNullOrWhiteSpace(request.Overrides.UserIdColumn)
                ? "Id"
                : request.Overrides.UserIdColumn!;

            IReadOnlyCollection<string>? includeColumns = request.Overrides.IncludeColumns;
            if (includeColumns is null || includeColumns.Count == 0)
            {
                includeColumns = Array.Empty<string>();
            }

            var matchingStrategy = request.Overrides.MatchingStrategy ?? UserMatchingStrategy.CaseInsensitiveEmail;
            var fallbackAssignment = request.Overrides.FallbackAssignment ?? UserFallbackAssignmentMode.Ignore;
            var fallbackTargets = request.Overrides.FallbackTargets ?? Array.Empty<UserIdentifier>();

            var schemaGraph = request.SchemaGraph ?? new ModelSchemaGraph(request.Extraction.Model);
            var context = new UatUsersContext(
                schemaGraph,
                artifacts,
                connectionFactory,
                schema,
                table,
                userIdColumn,
                includeColumns,
                userMapPath,
                request.Overrides.UatUserInventoryPath!,
                request.Overrides.QaUserInventoryPath!,
                request.Overrides.SnapshotPath,
                request.Overrides.UserEntityIdentifier,
                fromLiveMetadata: false,
                sourceFingerprint: BuildSourceFingerprint(connectionString),
                matchingStrategy: matchingStrategy,
                matchingAttribute: request.Overrides.MatchingAttribute,
                matchingRegexPattern: request.Overrides.MatchingRegexPattern,
                fallbackAssignment: fallbackAssignment,
                fallbackTargets: fallbackTargets,
                idempotentEmission: request.Overrides.IdempotentEmission,
                concurrency: request.Overrides.Concurrency);

            var pipeline = new UatUsersPipeline(_loggerFactory);
            _logger.LogInformation(
                "Executing uat-users pipeline for {Schema}.{Table} -> {Path}.",
                schema,
                table,
                Path.Combine(artifacts.Root, "uat-users"));

            await pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            return Result<UatUsersApplicationResult>.Success(new UatUsersApplicationResult(
                Executed: true,
                Context: context,
                Warnings: ImmutableArray<string>.Empty));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("uat-users pipeline execution cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "uat-users pipeline failed.");
            return Result<UatUsersApplicationResult>.Failure(ValidationError.Create(
                "pipeline.fullExport.uatUsers.failed",
                $"uat-users pipeline failed: {ex.GetBaseException().Message}"));
        }
    }

    private static string BuildSourceFingerprint(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource ?? string.Empty;
        var catalog = builder.InitialCatalog ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dataSource) && string.IsNullOrWhiteSpace(catalog))
        {
            return "unspecified";
        }

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return catalog;
        }

        if (string.IsNullOrWhiteSpace(catalog))
        {
            return dataSource;
        }

        return string.Create(
            dataSource.Length + catalog.Length + 1,
            (dataSource, catalog),
            static (span, state) =>
            {
                state.dataSource.AsSpan().CopyTo(span);
                span[state.dataSource.Length] = '/';
                state.catalog.AsSpan().CopyTo(span[(state.dataSource.Length + 1)..]);
            });
    }
}
