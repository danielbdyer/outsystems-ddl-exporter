using System.Collections.Immutable;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.DynamicData;
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

public record SqlProjectSynthesized(
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
    string SqlProjectPath)
    : EmissionReady(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts);

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
    string SqlProjectPath,
    SsdtSqlValidationSummary SqlValidation)
    : SqlProjectSynthesized(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlProjectPath);

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
    string SqlProjectPath,
    SsdtSqlValidationSummary SqlValidation,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<StaticEntityTableData> StaticSeedData,
    bool StaticSeedTopologicalOrderApplied)
    : SqlValidated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlProjectPath, SqlValidation);

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
    string SqlProjectPath,
    SsdtSqlValidationSummary SqlValidation,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<StaticEntityTableData> StaticSeedData,
    ImmutableArray<string> DynamicInsertScriptPaths,
    DynamicInsertOutputMode DynamicInsertOutputMode,
    bool StaticSeedTopologicalOrderApplied,
    bool DynamicInsertTopologicalOrderApplied)
    : StaticSeedsGenerated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlProjectPath, SqlValidation, StaticSeedScriptPaths, StaticSeedData, StaticSeedTopologicalOrderApplied);

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
    string SqlProjectPath,
    SsdtSqlValidationSummary SqlValidation,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<StaticEntityTableData> StaticSeedData,
    ImmutableArray<string> DynamicInsertScriptPaths,
    DynamicInsertOutputMode DynamicInsertOutputMode,
    bool StaticSeedTopologicalOrderApplied,
    bool DynamicInsertTopologicalOrderApplied,
    ImmutableArray<string> TelemetryPackagePaths)
    : DynamicInsertsGenerated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Validations, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlProjectPath, SqlValidation, StaticSeedScriptPaths, StaticSeedData, DynamicInsertScriptPaths, DynamicInsertOutputMode, StaticSeedTopologicalOrderApplied, DynamicInsertTopologicalOrderApplied);
