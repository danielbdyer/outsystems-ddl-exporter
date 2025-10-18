using System.Collections.Immutable;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Pipeline.Orchestration;

public sealed record BuildSsdtPipelineResult(
    ProfileSnapshot Profile,
    ImmutableArray<ProfilingInsight> ProfilingInsights,
    PolicyDecisionReport DecisionReport,
    OpportunitiesReport Opportunities,
    SsdtManifest Manifest,
    ImmutableArray<PipelineInsight> PipelineInsights,
    string DecisionLogPath,
    string OpportunitiesPath,
    string SafeScriptPath,
    string RemediationScriptPath,
    ImmutableArray<string> StaticSeedScriptPaths,
    EvidenceCacheResult? EvidenceCache,
    PipelineExecutionLog ExecutionLog,
    ImmutableArray<string> Warnings);
