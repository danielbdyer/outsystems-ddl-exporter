using System.Collections.Immutable;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;

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
    ValidationReport Validations,
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
    ValidationReport Validations,
    ImmutableArray<PipelineInsight> Insights,
    SsdtManifest Manifest,
    string DecisionLogPath,
    OpportunityArtifacts OpportunityArtifacts)
    : DecisionsSynthesized(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights);

public record SqlValidated(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    OpportunitiesReport Opportunities,
    ValidationReport Validations,
    ImmutableArray<PipelineInsight> Insights,
    SsdtManifest Manifest,
    string DecisionLogPath,
    OpportunityArtifacts OpportunityArtifacts,
    SsdtSqlValidationSummary SqlValidation)
    : EmissionReady(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts);

public record StaticSeedsGenerated(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    OpportunitiesReport Opportunities,
    ValidationReport Validations,
    ImmutableArray<PipelineInsight> Insights,
    SsdtManifest Manifest,
    string DecisionLogPath,
    OpportunityArtifacts OpportunityArtifacts,
    SsdtSqlValidationSummary SqlValidation,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<StaticEntityTableData> StaticSeedData)
    : SqlValidated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlValidation);

public record DynamicInsertsGenerated(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    OpportunitiesReport Opportunities,
    ValidationReport Validations,
    ImmutableArray<PipelineInsight> Insights,
    SsdtManifest Manifest,
    string DecisionLogPath,
    OpportunityArtifacts OpportunityArtifacts,
    SsdtSqlValidationSummary SqlValidation,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<StaticEntityTableData> StaticSeedData,
    ImmutableArray<string> DynamicInsertScriptPaths)
    : StaticSeedsGenerated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlValidation, StaticSeedScriptPaths, StaticSeedData);

public record TelemetryPackaged(
    BuildSsdtPipelineRequest Request,
    PipelineExecutionLogBuilder Log,
    PipelineBootstrapContext Bootstrap,
    EvidenceCacheResult? EvidenceCache,
    PolicyDecisionSet Decisions,
    PolicyDecisionReport Report,
    OpportunitiesReport Opportunities,
    ValidationReport Validations,
    ImmutableArray<PipelineInsight> Insights,
    SsdtManifest Manifest,
    string DecisionLogPath,
    OpportunityArtifacts OpportunityArtifacts,
    SsdtSqlValidationSummary SqlValidation,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<StaticEntityTableData> StaticSeedData,
    ImmutableArray<string> DynamicInsertScriptPaths,
    ImmutableArray<string> TelemetryPackagePaths)
    : DynamicInsertsGenerated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlValidation, StaticSeedScriptPaths, StaticSeedData, DynamicInsertScriptPaths);
