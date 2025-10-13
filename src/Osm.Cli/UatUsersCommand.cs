using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;

namespace Osm.Cli;

public sealed class UatUsersCommand
{
    private readonly IModelIngestionService _modelIngestionService;
    private readonly ILogger<UatUsersCommand> _logger;

    public UatUsersCommand(IModelIngestionService modelIngestionService, ILogger<UatUsersCommand> logger)
    {
        _modelIngestionService = modelIngestionService ?? throw new ArgumentNullException(nameof(modelIngestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> ExecuteAsync(UatUsersOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        try
        {
            IUserSchemaGraph schemaGraph;
            if (options.FromLiveMetadata)
            {
                if (string.IsNullOrWhiteSpace(options.UatConnectionString))
                {
                    _logger.LogError("--uat-conn must be supplied when --from-live is enabled.");
                    return 1;
                }

                var connectionFactory = new SqlConnectionFactory(options.UatConnectionString!, new SqlConnectionOptions(null, null, "osm-uat-users", null));
                schemaGraph = new LiveSchemaGraph(connectionFactory);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(options.ModelPath))
                {
                    _logger.LogError("--model is required unless --from-live is specified.");
                    return 1;
                }

                var loadResult = await _modelIngestionService.LoadFromFileAsync(options.ModelPath!, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (loadResult.IsFailure)
                {
                    LogErrors(loadResult.Errors);
                    return 1;
                }

                schemaGraph = new ModelSchemaGraph(loadResult.Value);
            }

            var artifacts = new UatUsersArtifacts(options.OutputDirectory);
            var userMapPath = options.UserMapPath ?? artifacts.GetDefaultUserMapPath();
            var context = new UatUsersContext(
                schemaGraph,
                artifacts,
                options.UserSchema,
                options.UserTable,
                options.UserIdColumn,
                options.IncludeColumns,
                userMapPath,
                options.FromLiveMetadata);

            var pipeline = new UatUsersPipeline();
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
}
