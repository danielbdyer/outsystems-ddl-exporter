using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

internal readonly record struct ProfilingProbeResult<T>(T Value, ProfilingProbeStatus Status);
