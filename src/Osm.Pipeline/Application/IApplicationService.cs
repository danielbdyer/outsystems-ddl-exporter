using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Application;

public interface IApplicationService<TInput, TResult>
{
    Task<Result<TResult>> RunAsync(TInput input, CancellationToken cancellationToken = default);
}
