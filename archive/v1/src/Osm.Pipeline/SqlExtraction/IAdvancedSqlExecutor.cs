using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.SqlExtraction;

public interface IAdvancedSqlExecutor
{
    Task<Result<long>> ExecuteAsync(
        AdvancedSqlRequest request,
        Stream destination,
        CancellationToken cancellationToken = default);
}
