using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Dmm;

public interface IDmmLens<in TSource>
{
    Task<Result<IAsyncEnumerable<DmmTable>>> ProjectAsync(
        TSource source,
        CancellationToken cancellationToken = default);
}
