using System.Collections.Immutable;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;

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
    PolicyDecisionReport Report)
    : EvidencePrepared(Request, Log, Bootstrap, EvidenceCache);

public record EmissionReady(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    SsdtManifest Manifest,
    string DecisionLogPath)
    : DecisionsSynthesized(Request, Log, Bootstrap, EvidenceCache, Decisions, Report);

public record StaticSeedsGenerated(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    SsdtManifest Manifest,
    string DecisionLogPath,
    ImmutableArray<string> StaticSeedScriptPaths)
    : EmissionReady(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Manifest, DecisionLogPath);
