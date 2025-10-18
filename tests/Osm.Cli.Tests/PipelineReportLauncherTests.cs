using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests;

public class PipelineReportLauncherTests
{
    [Fact]
    public async Task GenerateAsync_WritesReportWithArtifactLinksAndFallbackInsights()
    {
        using var output = new TempDirectory();
        var applicationResult = await CreateApplicationResultAsync(
            output,
            ImmutableArray<PipelineInsight>.Empty,
            includeDiff: true);

        var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, CancellationToken.None);

        Assert.True(File.Exists(reportPath));
        var html = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("manifest.json", html);
        Assert.Contains("policy-decisions.json", html);
        Assert.Contains("dmm-diff.json", html);
        Assert.Contains("AppCore", html);
        Assert.Contains("ExtBilling", html);
        Assert.Contains("No pipeline insights were generated", html);
    }

    [Fact]
    public async Task GenerateAsync_WithInsights_RendersInsightCards()
    {
        using var output = new TempDirectory();
        var insights = ImmutableArray.Create(
            new PipelineInsight(
                code: "IDX-001",
                title: "Missing supporting index",
                summary: "Unique candidate on Sales.Invoice.InvoiceNumber lacks a physical index.",
                severity: PipelineInsightSeverity.Warning,
                affectedObjects: ImmutableArray.Create("Sales.Invoice.InvoiceNumber"),
                suggestedAction: "Create a nonclustered index on InvoiceNumber to enforce uniqueness.",
                documentationUri: "https://contoso.example/insights/idx-001"),
            new PipelineInsight(
                code: "FK-REVIEW",
                title: "Foreign key skipped",
                summary: "CustomerRegion relationship skipped due to profiling orphans.",
                severity: PipelineInsightSeverity.Critical,
                affectedObjects: ImmutableArray.Create("dbo.Customer.RegionId"),
                suggestedAction: "Review and clean orphaned RegionId values before enabling the constraint."));

        var applicationResult = await CreateApplicationResultAsync(
            output,
            insights,
            includeDiff: false);

        var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, CancellationToken.None);

        Assert.True(File.Exists(reportPath));
        var html = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("Pipeline insights", html);
        Assert.Contains("severity-warning", html);
        Assert.Contains("severity-critical", html);
        Assert.Contains("IDX-001", html);
        Assert.Contains("View guidance", html);
        Assert.DoesNotContain("No pipeline insights were generated", html);
    }

    private static async Task<BuildSsdtApplicationResult> CreateApplicationResultAsync(
        TempDirectory output,
        ImmutableArray<PipelineInsight> insights,
        bool includeDiff)
    {
        var coverage = new SsdtCoverageSummary(
            CoverageBreakdown.Create(2, 2),
            CoverageBreakdown.Create(4, 4),
            CoverageBreakdown.Create(6, 6));
        var manifest = new SsdtManifest(
            new[]
            {
                new TableManifestEntry(
                    "AppCore",
                    "dbo",
                    "Customer",
                    "Modules/AppCore/Tables/dbo.Customer.sql",
                    new[] { "IX_Customer_Email" },
                    new[] { "FK_Customer_City" },
                    true),
                new TableManifestEntry(
                    "ExtBilling",
                    "dbo",
                    "Invoice",
                    "Modules/ExtBilling/Tables/dbo.Invoice.sql",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false)
            },
            new SsdtManifestOptions(true, false, false, 1),
            null,
            new SsdtEmissionMetadata("SHA256", "abc123"),
            Array.Empty<PreRemediationManifestEntry>(),
            coverage,
            Array.Empty<string>());

        var decisionReport = new PolicyDecisionReport(
            ImmutableArray.Create(
                new ColumnDecisionReport(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("Customer"), new ColumnName("Email")),
                    true,
                    false,
                    ImmutableArray<string>.Empty)),
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty.Add(
                "AppCore",
                new ModuleDecisionRollup(
                    1,
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    ImmutableDictionary<string, int>.Empty,
                    ImmutableDictionary<string, int>.Empty,
                    ImmutableDictionary<string, int>.Empty)),
            TighteningToggleSnapshot.Create(TighteningOptions.Default));

        var seedPaths = ImmutableArray.Create(
            Path.Combine(output.Path, "Seeds", "AppCore", "StaticEntities.seed.sql"),
            Path.Combine(output.Path, "Seeds", "ExtBilling", "StaticEntities.seed.sql"));

        foreach (var seedPath in seedPaths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
            await File.WriteAllTextAsync(seedPath, string.Empty);
        }

        var policyPath = Path.Combine(output.Path, "policy-decisions.json");
        await File.WriteAllTextAsync(policyPath, "{}");

        if (includeDiff)
        {
            await File.WriteAllTextAsync(Path.Combine(output.Path, "dmm-diff.json"), "{}");
        }

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<ChangeRisk, int>.Empty,
            ImmutableDictionary<ConstraintType, int>.Empty,
            DateTimeOffset.UnixEpoch);

        var opportunitiesPath = Path.Combine(output.Path, "opportunities.json");
        await File.WriteAllTextAsync(opportunitiesPath, "{}");

        var suggestionsRoot = Path.Combine(output.Path, "suggestions");
        Directory.CreateDirectory(suggestionsRoot);
        var safePath = Path.Combine(suggestionsRoot, "safe-to-apply.sql");
        var remediationPath = Path.Combine(suggestionsRoot, "needs-remediation.sql");
        await File.WriteAllTextAsync(safePath, string.Empty);
        await File.WriteAllTextAsync(remediationPath, string.Empty);

        var pipelineResult = new BuildSsdtPipelineResult(
            new ProfileSnapshot(
                ImmutableArray<ColumnProfile>.Empty,
                ImmutableArray<UniqueCandidateProfile>.Empty,
                ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
                ImmutableArray<ForeignKeyReality>.Empty),
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            manifest,
            insights.IsDefault ? ImmutableArray<PipelineInsight>.Empty : insights,
            policyPath,
            opportunitiesPath,
            safePath,
            string.Empty,
            remediationPath,
            string.Empty,
            seedPaths,
            null,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty);

        return new BuildSsdtApplicationResult(
            pipelineResult,
            "fixture",
            Path.Combine(output.Path, "profile.json"),
            output.Path,
            Path.Combine(output.Path, "model.json"),
            false,
            ImmutableArray<string>.Empty);
    }
}
