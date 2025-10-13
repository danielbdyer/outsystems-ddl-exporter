using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.UatUsers.Steps;

namespace Osm.Pipeline.UatUsers;

public sealed class UatUsersPipeline : IPipeline<UatUsersContext>
{
    private readonly IPipeline<UatUsersContext> _inner;

    public UatUsersPipeline()
    {
        _inner = new PipelineBuilder<UatUsersContext>()
            .Then(new DiscoverUserFkCatalogStep())
            .Then(new LoadAllowedUsersStep())
            .Then(new AnalyzeForeignKeyValuesStep())
            .Then(new PrepareUserMapStep())
            .Then(new EmitArtifactsStep())
            .Build();
    }

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        return _inner.ExecuteAsync(context, cancellationToken);
    }
}
