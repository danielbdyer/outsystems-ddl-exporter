using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.SqlExtraction;

public interface IAdvancedSqlExecutor
{
    Task<Result<string>> ExecuteAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default);
}
