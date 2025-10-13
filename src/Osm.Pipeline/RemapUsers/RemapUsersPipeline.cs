using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.RemapUsers.Steps;

namespace Osm.Pipeline.RemapUsers;

public sealed class RemapUsersPipeline : IPipeline<RemapUsersContext>
{
    private readonly IPipeline<RemapUsersContext> _inner;

    public RemapUsersPipeline()
    {
        _inner = new PipelineBuilder<RemapUsersContext>()
            .Then(new DiscoverUserFkCatalogStep())
            .Then(new CreateControlAndStagingSchemasStep())
            .Then(new StageSourceSnapshotsStep())
            .Then(new BuildUserMapStep())
            .Then(new RewriteStagingFksToUatUsersStep())
            .Then(new DryRunReportStep())
            .Then(new ConstraintWindowLoadStep(), context => !context.DryRun)
            .Then(new PostLoadValidationStep(), context => !context.DryRun)
            .Then(new EmitArtifactsStep())
            .Build();
    }

    public Task ExecuteAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        return _inner.ExecuteAsync(context, cancellationToken);
    }
}
