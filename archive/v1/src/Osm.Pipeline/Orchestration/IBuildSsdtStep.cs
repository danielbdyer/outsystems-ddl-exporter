using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Orchestration;

public interface IBuildSsdtStep<in TState, TNextState>
{
    Task<Result<TNextState>> ExecuteAsync(TState state, CancellationToken cancellationToken = default);
}
