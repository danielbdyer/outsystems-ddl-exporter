using System.Collections.Immutable;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed record BuildSsdtPipelineResult(
    ProfileSnapshot Profile,
    ImmutableArray<ProfilingInsight> ProfilingInsights,
    PolicyDecisionReport DecisionReport,
    SsdtManifest Manifest,
    ImmutableArray<PipelineInsight> PipelineInsights,
    string DecisionLogPath,
    ImmutableArray<string> StaticSeedScriptPaths,
    EvidenceCacheResult? EvidenceCache,
    PipelineExecutionLog ExecutionLog,
    ImmutableArray<string> Warnings);
