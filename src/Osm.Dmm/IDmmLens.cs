using System.Collections.Generic;
using System.Threading;
using Osm.Domain.Abstractions;

namespace Osm.Dmm;

public interface IDmmLens<in TSource>
{
    Result<IReadOnlyList<DmmTable>> Project(TSource source);

    IAsyncEnumerable<Result<DmmTable>> ProjectAsync(TSource source, CancellationToken cancellationToken = default);
}
