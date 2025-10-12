using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Application;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests;

public class PipelineReportLauncherTests
{
    [Fact]
    public async Task GenerateAsync_WritesReportWithArtifactLinks()
    {
        using var output = new TempDirectory();
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
                    new string[0],
                    new string[0],
                    false)
            },
            new SsdtManifestOptions(true, false, false, 1),
            null,
            new SsdtEmissionMetadata("SHA256", "abc123"),
            new PreRemediationManifestEntry[0],
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
            ImmutableArray<TighteningDiagnostic>.Empty);

        var seedPaths = ImmutableArray.Create(
            Path.Combine(output.Path, "Seeds", "AppCore", "StaticEntities.seed.sql"),
            Path.Combine(output.Path, "Seeds", "ExtBilling", "StaticEntities.seed.sql"));

        var pipelineResult = new BuildSsdtPipelineResult(
            new ProfileSnapshot(
                ImmutableArray<ColumnProfile>.Empty,
                ImmutableArray<UniqueCandidateProfile>.Empty,
                ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
                ImmutableArray<ForeignKeyReality>.Empty),
            decisionReport,
            manifest,
            Path.Combine(output.Path, "policy-decisions.json"),
            seedPaths,
            null,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty);

        foreach (var seedPath in seedPaths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
            await File.WriteAllTextAsync(seedPath, string.Empty);
        }

        await File.WriteAllTextAsync(Path.Combine(output.Path, "policy-decisions.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(output.Path, "dmm-diff.json"), "{}");

        var applicationResult = new BuildSsdtApplicationResult(
            pipelineResult,
            "fixture",
            Path.Combine(output.Path, "profile.json"),
            output.Path);

        var reportPath = await PipelineReportLauncher.GenerateAsync(applicationResult, CancellationToken.None);

        Assert.True(File.Exists(reportPath));
        var html = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("manifest.json", html);
        Assert.Contains("policy-decisions.json", html);
        Assert.Contains("dmm-diff.json", html);
        Assert.Contains("AppCore", html);
        Assert.Contains("ExtBilling", html);
    }
}
