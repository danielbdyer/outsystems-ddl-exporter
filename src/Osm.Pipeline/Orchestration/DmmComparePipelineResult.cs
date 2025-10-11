using Osm.Domain.Profiling;
using Osm.Dmm;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Orchestration;

public sealed record DmmComparePipelineResult(
    ProfileSnapshot Profile,
    DmmComparisonResult Comparison,
    string DiffArtifactPath,
    EvidenceCacheResult? EvidenceCache);
