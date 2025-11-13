using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;

namespace Osm.Cli;

public interface IUatUsersCommand
{
    Task<int> ExecuteAsync(UatUsersOptions options, CancellationToken cancellationToken);
}

public sealed class UatUsersCommand : IUatUsersCommand
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ILogger<UatUsersCommand> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public UatUsersCommand(
        IModelIngestionService modelIngestionService,
        ILogger<UatUsersCommand> logger,
        ILoggerFactory? loggerFactory = null)
    {
        _modelIngestionService = modelIngestionService ?? throw new ArgumentNullException(nameof(modelIngestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public async Task<int> ExecuteAsync(UatUsersOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        try
        {
            var configurationFields = new List<string>();
            if (options.Origins.ModelPathFromConfiguration)
            {
                configurationFields.Add("ModelPath");
            }

            if (options.Origins.ConnectionStringFromConfiguration)
            {
                configurationFields.Add("UatConnectionString");
            }

            if (options.Origins.FromLiveMetadataFromConfiguration)
            {
                configurationFields.Add("FromLiveMetadata");
            }

            if (options.Origins.UserSchemaFromConfiguration)
            {
                configurationFields.Add("UserSchema");
            }

            if (options.Origins.UserTableFromConfiguration)
            {
                configurationFields.Add("UserTable");
            }

            if (options.Origins.UserIdColumnFromConfiguration)
            {
                configurationFields.Add("UserIdColumn");
            }

            if (options.Origins.IncludeColumnsFromConfiguration)
            {
                configurationFields.Add("IncludeColumns");
            }

            if (options.Origins.OutputDirectoryFromConfiguration)
            {
                configurationFields.Add("OutputDirectory");
            }

            if (options.Origins.UserMapPathFromConfiguration)
            {
                configurationFields.Add("UserMapPath");
            }

            if (options.Origins.UatUserInventoryPathFromConfiguration)
            {
                configurationFields.Add("UatUserInventoryPath");
            }

            if (options.Origins.QaUserInventoryPathFromConfiguration)
            {
                configurationFields.Add("QaUserInventoryPath");
            }

            if (options.Origins.SnapshotPathFromConfiguration)
            {
                configurationFields.Add("SnapshotPath");
            }

            if (options.Origins.UserEntityIdentifierFromConfiguration)
            {
                configurationFields.Add("UserEntityIdentifier");
            }

            if (configurationFields.Count > 0)
            {
                _logger.LogInformation(
                    "Configuration defaults applied for: {Fields}.",
                    string.Join(", ", configurationFields));
            }

            _logger.LogInformation(
                "Executing uat-users command. OutputRoot={OutputRoot}, FromLiveMetadata={FromLive}, Snapshot={SnapshotPath}.",
                options.OutputDirectory,
                options.FromLiveMetadata,
                options.SnapshotPath ?? "<none>");
            _logger.LogInformation(
                "User configuration: Schema={Schema}, Table={Table}, IdColumn={Column}, IncludeColumns={IncludeCount}, EntityIdentifier={EntityIdentifier}.",
                options.UserSchema,
                options.UserTable,
                options.UserIdColumn,
                options.IncludeColumns.Length,
                options.UserEntityIdentifier ?? "<none>");
            _logger.LogInformation(
                "User inventories: UAT={UatInventory}, QA={QaInventory}.",
                options.UatUserInventoryPath,
                options.QaUserInventoryPath);

            if (string.IsNullOrWhiteSpace(options.UatConnectionString))
            {
                _logger.LogError("--uat-conn must be supplied.");
                return 1;
            }

            var sqlOptions = new SqlConnectionOptions(null, null, "osm-uat-users", null);
            var connectionFactory = new SqlConnectionFactory(options.UatConnectionString!, sqlOptions);

            IUserSchemaGraph schemaGraph;
            if (options.FromLiveMetadata)
            {
                _logger.LogInformation("Using live metadata from UAT database for schema discovery.");
                schemaGraph = new LiveSchemaGraph(connectionFactory);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(options.ModelPath))
                {
                    _logger.LogError("--model is required unless --from-live is specified.");
                    return 1;
                }

                _logger.LogInformation("Loading schema graph from model file {ModelPath}.", options.ModelPath);
                var loadResult = await _modelIngestionService.LoadFromFileAsync(options.ModelPath!, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (loadResult.IsFailure)
                {
                    LogErrors(loadResult.Errors);
                    return 1;
                }

                schemaGraph = new ModelSchemaGraph(loadResult.Value);
                _logger.LogInformation(
                    "Model file {ModelPath} loaded successfully.",
                    options.ModelPath ?? "<unspecified>");
            }

            var artifacts = new UatUsersArtifacts(options.OutputDirectory);
            string userMapPath = options.UserMapPath ?? artifacts.GetDefaultUserMapPath();
            var sourceFingerprint = BuildSourceFingerprint(options.UatConnectionString!);
            _logger.LogInformation(
                "Resolved artifacts root to {ArtifactsRoot}; user map path is {UserMapPath}; source fingerprint is {SourceFingerprint}.",
                Path.Combine(artifacts.Root, "uat-users"),
                userMapPath,
                sourceFingerprint);
            var context = new UatUsersContext(
                schemaGraph,
                artifacts,
                connectionFactory,
                options.UserSchema,
                options.UserTable,
                options.UserIdColumn,
                options.IncludeColumns,
                userMapPath,
                options.UatUserInventoryPath,
                options.QaUserInventoryPath,
                options.SnapshotPath,
                options.UserEntityIdentifier,
                options.FromLiveMetadata,
                sourceFingerprint);

            var pipeline = new UatUsersPipeline(_loggerFactory);
            _logger.LogInformation("Invoking uat-users pipeline.");
            await pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("uat-users artifacts written to {Path}.", Path.Combine(artifacts.Root, "uat-users"));
            return 0;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("uat-users command cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "uat-users command failed.");
            return 1;
        }
    }

    private void LogErrors(IReadOnlyCollection<ValidationError> errors)
    {
        foreach (var error in errors)
        {
            _logger.LogError("{Code}: {Message}", error.Code, error.Message);
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
