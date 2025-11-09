using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tightening.Validations;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class SchemaApplyOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidateThenApplyMode_AppliesSeedsWhenValidationPasses()
    {
        var safeScript = Path.Combine(Path.GetTempPath(), "apply-safe.sql");
        var seedScript = Path.Combine(Path.GetTempPath(), "static-seed.sql");
        var build = CreateBuildResult(
            safeScript,
            seedScript,
            contradictionCount: 0);

        var applier = new RecordingSchemaDataApplier
        {
            Outcome = Result<SchemaDataApplyOutcome>.Success(
                new SchemaDataApplyOutcome(
                    ImmutableArray.Create(safeScript),
                    ImmutableArray.Create(seedScript),
                    ExecutedBatchCount: 3,
                    Duration: TimeSpan.FromSeconds(2),
                    MaxBatchSizeBytes: 4096,
                    StreamingEnabled: true,
                    StaticSeedValidation: StaticSeedValidationSummary.Success))
        };

        var orchestrator = new SchemaApplyOrchestrator(applier);
        var options = new SchemaApplyOptions(
            Enabled: true,
            ConnectionString: "Server=(local);Database=Osm;Trusted_Connection=True;",
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            CommandTimeoutSeconds: 30,
            ApplySafeScript: true,
            ApplyStaticSeeds: true,
            StaticSeedSynchronizationMode: StaticSeedSynchronizationMode.ValidateThenApply);

        var result = await orchestrator.ExecuteAsync(build, options);

        Assert.True(result.IsSuccess);
        var apply = result.Value;

        Assert.NotNull(applier.LastRequest);
        Assert.Equal(StaticSeedSynchronizationMode.ValidateThenApply, applier.LastRequest!.StaticSeedSynchronizationMode);
        Assert.Contains(seedScript, applier.LastRequest.SeedScriptPaths);

        Assert.True(apply.Attempted);
        Assert.True(apply.StaticSeedsApplied);
        Assert.True(apply.StaticSeedValidation.Attempted);
        Assert.False(apply.StaticSeedValidation.Failed);
        Assert.Equal(StaticSeedSynchronizationMode.ValidateThenApply, apply.StaticSeedSynchronizationMode);
        Assert.Empty(apply.Warnings);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidationFailure_SurfacesWarningAndSkipsSeeds()
    {
        var safeScript = Path.Combine(Path.GetTempPath(), "apply-safe.sql");
        var seedScript = Path.Combine(Path.GetTempPath(), "static-seed.sql");
        var build = CreateBuildResult(
            safeScript,
            seedScript,
            contradictionCount: 0);

        var driftMessage = "Static entity seed data drift detected.";
        var applier = new RecordingSchemaDataApplier
        {
            Outcome = Result<SchemaDataApplyOutcome>.Success(
                new SchemaDataApplyOutcome(
                    ImmutableArray.Create(safeScript),
                    ImmutableArray<string>.Empty,
                    ExecutedBatchCount: 1,
                    Duration: TimeSpan.FromSeconds(1),
                    MaxBatchSizeBytes: 1024,
                    StreamingEnabled: true,
                    StaticSeedValidation: StaticSeedValidationSummary.Failure(driftMessage)))
        };

        var orchestrator = new SchemaApplyOrchestrator(applier);
        var options = new SchemaApplyOptions(
            Enabled: true,
            ConnectionString: "Server=(local);Database=Osm;Trusted_Connection=True;",
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            CommandTimeoutSeconds: 30,
            ApplySafeScript: true,
            ApplyStaticSeeds: true,
            StaticSeedSynchronizationMode: StaticSeedSynchronizationMode.ValidateThenApply);

        var result = await orchestrator.ExecuteAsync(build, options);

        Assert.True(result.IsSuccess);
        var apply = result.Value;

        Assert.NotNull(applier.LastRequest);
        Assert.Equal(StaticSeedSynchronizationMode.ValidateThenApply, apply.StaticSeedSynchronizationMode);
        Assert.True(apply.StaticSeedValidation.Attempted);
        Assert.True(apply.StaticSeedValidation.Failed);
        Assert.Contains(driftMessage, apply.Warnings);
        Assert.DoesNotContain(seedScript, apply.AppliedSeedScripts);
        Assert.Contains(seedScript, apply.SkippedScripts);
        Assert.False(apply.StaticSeedsApplied);
    }

    private static BuildSsdtPipelineResult CreateBuildResult(
        string safeScriptPath,
        string seedScriptPath,
        int contradictionCount)
    {
        var profile = ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json"));

        var manifest = new SsdtManifest(
            Array.Empty<TableManifestEntry>(),
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(0, 0, 0),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());

        var toggleSnapshot = TighteningToggleSnapshot.Create(TighteningOptions.Default);
        var togglePrecedence = toggleSnapshot.ToExportDictionary().ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

        var decisionReport = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            togglePrecedence,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            toggleSnapshot);

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty.Add(OpportunityCategory.Contradiction, contradictionCount),
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        return new BuildSsdtPipelineResult(
            profile,
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            Path.Combine(Path.GetTempPath(), "decision-log.json"),
            Path.Combine(Path.GetTempPath(), "opportunities.json"),
            Path.Combine(Path.GetTempPath(), "validations.json"),
            safeScriptPath,
            "PRINT 'safe';",
            Path.Combine(Path.GetTempPath(), "remediation.sql"),
            "PRINT 'remediation';",
            ImmutableArray.Create(seedScriptPath),
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            ImmutableArray<string>.Empty,
            MultiEnvironmentReport: null);
    }

    private sealed class RecordingSchemaDataApplier : ISchemaDataApplier
    {
        public SchemaDataApplyRequest? LastRequest { get; private set; }

        public Result<SchemaDataApplyOutcome> Outcome { get; set; } = Result<SchemaDataApplyOutcome>.Success(
            new SchemaDataApplyOutcome(
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ExecutedBatchCount: 0,
                Duration: TimeSpan.Zero,
                MaxBatchSizeBytes: 0,
                StreamingEnabled: true,
                StaticSeedValidation: StaticSeedValidationSummary.NotAttempted));

        public Task<Result<SchemaDataApplyOutcome>> ApplyAsync(
            SchemaDataApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Outcome);
        }
    }
}
