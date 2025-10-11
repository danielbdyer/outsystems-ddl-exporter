using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public interface IDataProfiler
{
    Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<Result<ProfileObservation>> StreamAsync(CancellationToken cancellationToken = default);
}
