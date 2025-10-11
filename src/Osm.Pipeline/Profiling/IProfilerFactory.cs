using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Profiling;

public interface IProfilerFactory
{
    Result<IDataProfiler> Create(BuildSsdtPipelineRequest request, OsmModel model);
}
