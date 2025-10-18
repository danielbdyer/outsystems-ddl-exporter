using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Opportunities = Osm.Validation.Tightening.Opportunities;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class OpportunityLogWriterTests
{
    [Fact]
    public async Task WriteAsync_PersistsDeterministicArtifacts()
    {
        var report = CreateReport();
        var writer = new OpportunityLogWriter();

        using var directory = new TempDirectory();
        var artifacts = await writer.WriteAsync(directory.Path, report);

        var expectedJson = await File.ReadAllTextAsync(FixtureFile.GetPath("opportunities/opportunities.json"));
        var expectedSafe = await File.ReadAllTextAsync(FixtureFile.GetPath("opportunities/safe-to-apply.sql"));
        var expectedRemediation = await File.ReadAllTextAsync(FixtureFile.GetPath("opportunities/needs-remediation.sql"));

        Assert.Equal(expectedJson, await File.ReadAllTextAsync(artifacts.ReportPath));
        Assert.Equal(expectedSafe, await File.ReadAllTextAsync(artifacts.SafeScriptPath));
        Assert.Equal(expectedRemediation, await File.ReadAllTextAsync(artifacts.RemediationScriptPath));
        Assert.Equal(expectedSafe, artifacts.SafeScript);
        Assert.Equal(expectedRemediation, artifacts.RemediationScript);
    }

    private static OpportunitiesReport CreateReport()
    {
        var capture = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var probeStatus = ProfilingProbeStatus.CreateSucceeded(capture, 100);

        var notNullOpportunity = new Opportunities.Opportunity(
            Opportunities.ConstraintType.NotNull,
            Opportunities.ChangeRisk.SafeToApply,
            "dbo",
            "OSUSR_ABC_CUSTOMER",
            "Email",
            ImmutableArray.Create("ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER]\n    ALTER COLUMN [Email] NVARCHAR(255) NOT NULL;"),
            ImmutableArray.Create("Null probe succeeded."),
            ImmutableArray.Create(
                "Rows=100",
                "Nulls=0 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)"),
            new Opportunities.OpportunityMetrics(false, true, true, false, false),
            ImmutableArray.Create(
                new Opportunities.ColumnAnalysis(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ABC_CUSTOMER"), new ColumnName("EMAIL")),
                    "Customers",
                    "Customer",
                    "Email",
                    "Text",
                    "NVARCHAR(255)",
                    false,
                    false,
                    100,
                    0,
                    probeStatus,
                    false,
                    null,
                    false,
                    true,
                    null)));

        var uniqueOpportunity = new Opportunities.Opportunity(
            Opportunities.ConstraintType.Unique,
            Opportunities.ChangeRisk.NeedsRemediation,
            "dbo",
            "OSUSR_ABC_ORDER",
            "IX_OSUSR_ABC_ORDER_OrderNumber",
            ImmutableArray.Create(
                "CREATE UNIQUE INDEX [IX_OSUSR_ABC_ORDER_OrderNumber] ON [dbo].[OSUSR_ABC_ORDER] ([OrderNumber]);"),
            ImmutableArray.Create("Duplicate values detected."),
            ImmutableArray.Create(
                "Unique duplicates=True (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)"),
            new Opportunities.OpportunityMetrics(true, true, false, true, null),
            ImmutableArray.Create(
                new Opportunities.ColumnAnalysis(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ABC_ORDER"), new ColumnName("ORDERNUMBER")),
                    "Orders",
                    "Order",
                    "OrderNumber",
                    "Text",
                    "NVARCHAR(50)",
                    false,
                    false,
                    100,
                    0,
                    probeStatus,
                    true,
                    probeStatus,
                    null,
                    false,
                    null)));

        var riskCounts = ImmutableDictionary.CreateBuilder<Opportunities.ChangeRisk, int>();
        riskCounts[Opportunities.ChangeRisk.SafeToApply] = 1;
        riskCounts[Opportunities.ChangeRisk.NeedsRemediation] = 1;

        var constraintCounts = ImmutableDictionary.CreateBuilder<Opportunities.ConstraintType, int>();
        constraintCounts[Opportunities.ConstraintType.NotNull] = 1;
        constraintCounts[Opportunities.ConstraintType.Unique] = 1;

        return new OpportunitiesReport(
            ImmutableArray.Create(notNullOpportunity, uniqueOpportunity),
            riskCounts.ToImmutable(),
            constraintCounts.ToImmutable(),
            capture);
    }
}
