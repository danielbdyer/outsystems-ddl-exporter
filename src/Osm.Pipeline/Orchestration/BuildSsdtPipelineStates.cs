using System.Collections.Immutable;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;

namespace Osm.Pipeline.Orchestration;

public record PipelineInitialized(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log);

public record BootstrapCompleted(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap)
    : PipelineInitialized(Request, Log);

public record EvidencePrepared(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache)
    : BootstrapCompleted(Request, Log, Bootstrap);

public record DecisionsSynthesized(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    OpportunitiesReport Opportunities,
    ImmutableArray<PipelineInsight> Insights)
    : EvidencePrepared(Request, Log, Bootstrap, EvidenceCache);

public record EmissionReady(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    OpportunitiesReport Opportunities,
    ImmutableArray<PipelineInsight> Insights,
    SsdtManifest Manifest,
    string DecisionLogPath,
    OpportunityArtifacts OpportunityArtifacts)
    : DecisionsSynthesized(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Insights);

public record StaticSeedsGenerated(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    OpportunitiesReport Opportunities,
    ImmutableArray<PipelineInsight> Insights,
    SsdtManifest Manifest,
    string DecisionLogPath,
    OpportunityArtifacts OpportunityArtifacts,
    ImmutableArray<string> StaticSeedScriptPaths)
    : EmissionReady(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Insights, Manifest, DecisionLogPath, OpportunityArtifacts);
