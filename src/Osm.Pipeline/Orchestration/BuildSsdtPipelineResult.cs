using System.Collections.Immutable;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Evidence;
using Osm.Validation.Tightening;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;

namespace Osm.Pipeline.Orchestration;

public sealed record BuildSsdtPipelineResult(
    ProfileSnapshot Profile,
    ImmutableArray<ProfilingInsight> ProfilingInsights,
    PolicyDecisionReport DecisionReport,
    OpportunitiesReport Opportunities,
    SsdtManifest Manifest,
    ImmutableDictionary<string, ModuleManifestRollup> ModuleManifestRollups,
    ImmutableArray<PipelineInsight> PipelineInsights,
    string DecisionLogPath,
    string OpportunitiesPath,
    string SafeScriptPath,
    string SafeScript,
    string RemediationScriptPath,
    string RemediationScript,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<string> TelemetryPackagePaths,
    SsdtSqlValidationSummary SqlValidation,
    EvidenceCacheResult? EvidenceCache,
    PipelineExecutionLog ExecutionLog,
    ImmutableArray<string> Warnings);

public sealed record ModuleManifestRollup(int TableCount, int IndexCount, int ForeignKeyCount)
{
    public static ModuleManifestRollup Empty { get; } = new(0, 0, 0);
}
