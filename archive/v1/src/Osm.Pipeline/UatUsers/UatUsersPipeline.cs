using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Pipeline.UatUsers.Steps;

namespace Osm.Pipeline.UatUsers;

public sealed class UatUsersPipeline : IPipeline<UatUsersContext>
{
    private readonly IPipeline<UatUsersContext> _inner;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<UatUsersPipeline> _logger;

    public UatUsersPipeline(ILoggerFactory? loggerFactory = null)
        : this(loggerFactory, valueProvider: null, snapshotStore: null)
    {
    }

    public UatUsersPipeline(
        ILoggerFactory? loggerFactory,
        IUserForeignKeyValueProvider? valueProvider,
        IUserForeignKeySnapshotStore? snapshotStore)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<UatUsersPipeline>();

        var provider = valueProvider ?? new SqlUserForeignKeyValueProvider(_loggerFactory.CreateLogger<SqlUserForeignKeyValueProvider>());
        var store = snapshotStore ?? new FileUserForeignKeySnapshotStore(_loggerFactory.CreateLogger<FileUserForeignKeySnapshotStore>());

        _inner = new PipelineBuilder<UatUsersContext>(_loggerFactory)
            .Then(new DiscoverUserFkCatalogStep(_loggerFactory.CreateLogger<DiscoverUserFkCatalogStep>()))
            .Then(new LoadUatUserInventoryStep(_loggerFactory.CreateLogger<LoadUatUserInventoryStep>()))
            .Then(new LoadQaUserInventoryStep(_loggerFactory.CreateLogger<LoadQaUserInventoryStep>()))
            .Then(new AnalyzeForeignKeyValuesStep(
                provider,
                store,
                _loggerFactory.CreateLogger<AnalyzeForeignKeyValuesStep>()))
            .Then(new ApplyMatchingStrategyStep(_loggerFactory.CreateLogger<ApplyMatchingStrategyStep>()))
            .Then(new PrepareUserMapStep(_loggerFactory.CreateLogger<PrepareUserMapStep>()))
            .Then(new ValidateUserMapStep(_loggerFactory.CreateLogger<ValidateUserMapStep>()))
            .Then(new EmitArtifactsStep(_loggerFactory.CreateLogger<EmitArtifactsStep>()))
            .Build();
    }

    public async Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation(
            "Starting uat-users pipeline. Source={SourceFingerprint}, UserSchema={Schema}, UserTable={Table}.",
            context.SourceFingerprint,
            context.UserSchema,
            context.UserTable);

        try
        {
            await _inner.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Completed uat-users pipeline. CatalogCount={CatalogCount}, AllowedCount={AllowedCount}, OrphanCount={OrphanCount}.",
                context.UserFkCatalog.Count,
                context.AllowedUserIds.Count,
                context.OrphanUserIds.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("uat-users pipeline execution cancelled.");
            throw;
        }
    }
}
