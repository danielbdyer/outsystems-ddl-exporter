using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Profiling;

public interface IDataProfilerFactory
{
    Result<IDataProfiler> Create(BuildSsdtPipelineRequest request, OsmModel model);
}
