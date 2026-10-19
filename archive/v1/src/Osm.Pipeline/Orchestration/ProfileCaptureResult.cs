using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;

namespace Osm.Pipeline.Orchestration;

public sealed record ProfileCaptureResult(
    ProfileSnapshot Snapshot,
    MultiEnvironmentProfileReport? MultiEnvironmentReport);
