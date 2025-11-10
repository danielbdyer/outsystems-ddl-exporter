using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Emission;

namespace Osm.Pipeline.DynamicData;

public interface IDynamicEntityDataProvider
{
    Task<Result<DynamicEntityDataset>> ExtractAsync(
        SqlDynamicEntityExtractionRequest request,
        CancellationToken cancellationToken = default);
}
