namespace Osm.Pipeline.Profiling;

public interface IMultiEnvironmentProfiler
{
    MultiEnvironmentProfileReport? Report { get; }
}
