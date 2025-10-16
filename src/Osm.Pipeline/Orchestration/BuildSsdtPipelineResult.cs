using System.Collections.Immutable;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed record BuildSsdtPipelineResult(
    ProfileSnapshot Profile,
    ImmutableArray<SqlProfilerInsight> Insights,
    PolicyDecisionReport DecisionReport,
    SsdtManifest Manifest,
    string DecisionLogPath,
    ImmutableArray<string> StaticSeedScriptPaths,
    EvidenceCacheResult? EvidenceCache,
    PipelineExecutionLog ExecutionLog,
    ImmutableArray<string> Warnings);
