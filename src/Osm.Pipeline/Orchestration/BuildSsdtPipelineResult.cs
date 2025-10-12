using System.Collections.Immutable;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed record BuildSsdtPipelineResult(
    ProfileSnapshot Profile,
    PolicyDecisionReport DecisionReport,
    SsdtManifest Manifest,
    string DecisionLogPath,
    ImmutableArray<string> StaticSeedScriptPaths,
    EvidenceCacheResult? EvidenceCache,
    PipelineExecutionLog ExecutionLog,
    ImmutableArray<string> Warnings);
