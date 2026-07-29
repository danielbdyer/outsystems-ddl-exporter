using System.Collections.Immutable;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;

namespace Osm.Pipeline.Orchestration;

/// <summary>
/// Accumulating state threaded through the build-SSDT pipeline steps. Replaces the
/// former 13-record inheritance chain; each step returns `state with { ... }` setting
/// only the fields it produces. Fields populated by later steps default to null!/empty
/// and are guaranteed set by the time a downstream step or the final result reads them
/// (the step order in BuildSsdtPipeline enforces this at runtime, as the old chain did
/// at compile time).
/// </summary>
public sealed record BuildSsdtState
{
    public required BuildSsdtPipelineRequest Request { get; init; }
    public required PipelineExecutionLogBuilder Log { get; init; }
    public PipelineBootstrapContext Bootstrap { get; init; } = null!;
    public EvidenceCacheResult? EvidenceCache { get; init; }
    public PolicyDecisionSet Decisions { get; init; } = null!;
    public PolicyDecisionReport Report { get; init; } = null!;
    public OpportunitiesReport Opportunities { get; init; } = null!;
    public ValidationReport Validations { get; init; } = null!;
    public ImmutableArray<PipelineInsight> Insights { get; init; } = ImmutableArray<PipelineInsight>.Empty;
    public SsdtManifest Manifest { get; init; } = null!;
    public string DecisionLogPath { get; init; } = string.Empty;
    public OpportunityArtifacts OpportunityArtifacts { get; init; } = null!;
    public string SqlProjectPath { get; init; } = string.Empty;
    public SsdtSqlValidationSummary SqlValidation { get; init; } = null!;
    public ImmutableArray<string> StaticSeedScriptPaths { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<StaticEntityTableData> StaticSeedData { get; init; } = ImmutableArray<StaticEntityTableData>.Empty;
    public bool StaticSeedTopologicalOrderApplied { get; init; }
    public EntityDependencyOrderingMode StaticSeedOrderingMode { get; init; }
    public ImmutableArray<string> DynamicInsertScriptPaths { get; init; } = ImmutableArray<string>.Empty;
    public DynamicInsertOutputMode DynamicInsertOutputMode { get; init; }
    public bool DynamicInsertTopologicalOrderApplied { get; init; }
    public EntityDependencyOrderingMode DynamicInsertOrderingMode { get; init; }
    public string? BootstrapSnapshotPath { get; init; }
    public bool BootstrapTopologicalOrderApplied { get; init; }
    public EntityDependencyOrderingMode BootstrapOrderingMode { get; init; }
    public int BootstrapEntityCount { get; init; }
    public string? PostDeploymentTemplatePath { get; init; }
    public ImmutableArray<string> TelemetryPackagePaths { get; init; } = ImmutableArray<string>.Empty;
}
