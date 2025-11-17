using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Validation.Tightening;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;
using Opportunity = Osm.Validation.Tightening.Opportunities.Opportunity;
using OpportunityType = Osm.Validation.Tightening.Opportunities.OpportunityType;
using OpportunityDisposition = Osm.Validation.Tightening.Opportunities.OpportunityDisposition;
using OpportunityCategory = Osm.Validation.Tightening.Opportunities.OpportunityCategory;
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
            includeDiff: true,
            sqlValidation: null);

        var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, CancellationToken.None);

        Assert.True(File.Exists(reportPath));
        var html = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("manifest.json", html);
        Assert.Contains("policy-decisions.json", html);
        Assert.Contains("dmm-diff.json", html);
        Assert.Contains("AppCore", html);
        Assert.Contains("ExtBilling", html);
        Assert.Contains("Tightening toggles", html);
        Assert.Contains("policy.mode", html);
        Assert.Contains("EvidenceGated", html);
        Assert.Contains("<th>Columns</th>", html);
        Assert.Contains("No pipeline insights were generated", html);
        Assert.Contains("SQL validation", html);
        Assert.Contains("<strong>Files validated:</strong> 2", html);
        Assert.Contains("<strong>Files with errors:</strong> 0", html);
        Assert.Contains("<strong>Total errors:</strong> 0", html);
        Assert.Contains("No SQL validation errors were detected.", html);
        Assert.Contains("decision-index", html);
        Assert.Contains("Column decisions", html);
        Assert.Contains("id=\"module-appcore\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"column-dbo-customer-email\"", html, StringComparison.OrdinalIgnoreCase);
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
            includeDiff: false,
            sqlValidation: null);

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

    [Fact]
    public async Task GenerateAsync_WithSqlValidationErrors_RendersSampleLinks()
    {
        using var output = new TempDirectory();
        var issue = SsdtSqlValidationIssue.Create(
            "Modules/AppCore/dbo.Customer.sql",
            new[]
            {
                SsdtSqlValidationError.Create(102, 0, 16, 7, 15, "Incorrect syntax near ')'.")
            });
        var summary = SsdtSqlValidationSummary.Create(2, new[] { issue });

        var applicationResult = await CreateApplicationResultAsync(
            output,
            ImmutableArray<PipelineInsight>.Empty,
            includeDiff: false,
            sqlValidation: summary);

        var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, CancellationToken.None);

        Assert.True(File.Exists(reportPath));
        var html = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("SQL validation", html);
        Assert.Contains("<strong>Files validated:</strong> 2", html);
        Assert.Contains("<strong>Files with errors:</strong> 1", html);
        Assert.Contains("<strong>Total errors:</strong> 1", html);
        Assert.Contains("Incorrect syntax near", html);
        Assert.Contains("href=\"Modules/AppCore/dbo.Customer.sql\"", html);
    }

    private static async Task<BuildSsdtApplicationResult> CreateApplicationResultAsync(
        TempDirectory output,
        ImmutableArray<PipelineInsight> insights,
        bool includeDiff,
        SsdtSqlValidationSummary? sqlValidation)
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
                    "Modules/AppCore/dbo.Customer.sql",
                    new[] { "IX_Customer_Email" },
                    new[] { "FK_Customer_City_CityId" },
                    true),
                new TableManifestEntry(
                    "ExtBilling",
                    "dbo",
                    "Invoice",
                    "Modules/ExtBilling/dbo.Invoice.sql",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false)
            },
            new SsdtManifestOptions(true, false, false, 1),
            null,
            new SsdtEmissionMetadata("SHA256", "abc123"),
            Array.Empty<PreRemediationManifestEntry>(),
            coverage,
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());

        var columnCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("Customer"), new ColumnName("Email"));
        var decisionReport = new PolicyDecisionReport(
            ImmutableArray.Create(
                new ColumnDecisionReport(
                    columnCoordinate,
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
            ImmutableDictionary<string, ToggleExportValue>.Empty.Add(
                TighteningToggleKeys.PolicyMode,
                new ToggleExportValue(TighteningMode.EvidenceGated, ToggleSource.Configuration)),
            ImmutableDictionary<string, string>.Empty.Add(
                columnCoordinate.ToString(),
                "AppCore"),
            ImmutableDictionary<string, string>.Empty,
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
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty,
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UnixEpoch);

        var validations = ValidationReport.Empty(DateTimeOffset.UnixEpoch);

        var opportunitiesPath = Path.Combine(output.Path, "opportunities.json");
        await File.WriteAllTextAsync(opportunitiesPath, "{}");

        var validationsPath = Path.Combine(output.Path, "validations.json");
        await File.WriteAllTextAsync(validationsPath, "{}");

        var suggestionsRoot = Path.Combine(output.Path, "suggestions");
        Directory.CreateDirectory(suggestionsRoot);
        var safePath = Path.Combine(suggestionsRoot, "safe-to-apply.sql");
        var remediationPath = Path.Combine(suggestionsRoot, "needs-remediation.sql");
        await File.WriteAllTextAsync(safePath, string.Empty);
        await File.WriteAllTextAsync(remediationPath, string.Empty);

        var sqlSummary = sqlValidation ?? SsdtSqlValidationSummary.Create(
            manifest.Tables.Count,
            Array.Empty<SsdtSqlValidationIssue>());

        var pipelineResult = new BuildSsdtPipelineResult(
            new ProfileSnapshot(
                ImmutableArray<ColumnProfile>.Empty,
                ImmutableArray<UniqueCandidateProfile>.Empty,
                ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
                ImmutableArray<ForeignKeyReality>.Empty),
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty
                .Add("AppCore", new ModuleManifestRollup(1, 1, 1))
                .Add("ExtBilling", new ModuleManifestRollup(1, 0, 0)),
            insights.IsDefault ? ImmutableArray<PipelineInsight>.Empty : insights,
            policyPath,
            opportunitiesPath,
            validationsPath,
            safePath,
            string.Empty,
            remediationPath,
            string.Empty,
            Path.Combine(output.Path, "OutSystemsModel.sqlproj"),
            seedPaths,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            sqlSummary,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            DynamicInsertOutputMode: DynamicInsertOutputMode.PerEntity,
            ImmutableArray<DynamicEntityTableReconciliation>.Empty,
            ImmutableArray<string>.Empty,
            MultiEnvironmentProfileReport.Empty);

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
