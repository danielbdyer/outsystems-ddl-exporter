using System.Threading;
using System.Threading.Tasks;

namespace Osm.LoadHarness;

public interface ILoadHarnessRunner
{
    Task<LoadHarnessReport> RunAsync(LoadHarnessOptions options, CancellationToken cancellationToken = default);
}
