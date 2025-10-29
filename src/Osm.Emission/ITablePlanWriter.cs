using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Emission;

public interface ITablePlanWriter
{
    Task WriteAsync(
        IReadOnlyList<TableEmissionPlan> plans,
        int moduleParallelism,
        CancellationToken cancellationToken);
}
