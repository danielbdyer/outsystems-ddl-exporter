using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), "/");
        fileSystem.AddDirectory("/work");
        fileSystem.Directory.SetCurrentDirectory("/work");
        var writer = new OpportunityLogWriter(fileSystem);

        var result = await writer.WriteAsync("/work/output", report);
        Assert.True(result.IsSuccess);
        var artifacts = result.Value;

        var expectedJson = await File.ReadAllTextAsync(FixtureFile.GetPath("opportunities/opportunities.json"));
        var expectedSafe = await File.ReadAllTextAsync(FixtureFile.GetPath("opportunities/safe-to-apply.sql"));
        var expectedRemediation = await File.ReadAllTextAsync(FixtureFile.GetPath("opportunities/needs-remediation.sql"));

        var actualJson = fileSystem.File.ReadAllText(artifacts.ReportPath);
        var actualReport = JsonSerializer.Deserialize<OpportunitiesReport>(
            actualJson,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
        Assert.NotNull(actualReport);

        var expectedOpportunities = report.Opportunities;
        var actualOpportunities = actualReport!.Opportunities;
        Assert.Equal(expectedOpportunities.Length, actualOpportunities.Length);
        for (var i = 0; i < expectedOpportunities.Length; i++)
        {
            var expected = expectedOpportunities[i];
            var actual = actualOpportunities[i];
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.Disposition, actual.Disposition);
            Assert.Equal(expected.Evidence, actual.Evidence);
            Assert.Equal(expected.Statements, actual.Statements);
        }
        Assert.Equal(
            NormalizeEvidenceSections(expectedSafe),
            NormalizeEvidenceSections(fileSystem.File.ReadAllText(artifacts.SafeScriptPath)));
        Assert.Equal(
            NormalizeEvidenceSections(expectedRemediation),
            NormalizeEvidenceSections(fileSystem.File.ReadAllText(artifacts.RemediationScriptPath)));
        Assert.Equal(NormalizeEvidenceSections(expectedSafe), NormalizeEvidenceSections(artifacts.SafeScript));
        Assert.Equal(NormalizeEvidenceSections(expectedRemediation), NormalizeEvidenceSections(artifacts.RemediationScript));
    }

    private static OpportunitiesReport CreateReport()
    {
        var capture = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var probeStatus = ProfilingProbeStatus.CreateSucceeded(capture, 100);

        var notNullOpportunity = Opportunities.Opportunity.Create(
            Opportunities.OpportunityType.Nullability,
            "NOT NULL",
            "Enforce NOT NULL constraint.",
            ChangeRisk.Low("Column qualifies for NOT NULL enforcement."),
            ImmutableArray.Create(
                "Rows=100",
                "Nulls=0 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)"),
            column: new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ABC_CUSTOMER"), new ColumnName("EMAIL")),
            disposition: Opportunities.OpportunityDisposition.ReadyToApply,
            statements: ImmutableArray.Create("ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER]\n    ALTER COLUMN [Email] NVARCHAR(255) NOT NULL;"),
            rationales: ImmutableArray.Create("Null probe succeeded."),
            evidenceSummary: new Opportunities.OpportunityEvidenceSummary(false, true, true, false, false),
            columns: ImmutableArray.Create(
                new Opportunities.OpportunityColumn(
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
                    null)),
            schema: "dbo",
            table: "OSUSR_ABC_CUSTOMER",
            constraintName: "Email");

        var uniqueOpportunity = Opportunities.Opportunity.Create(
            Opportunities.OpportunityType.UniqueIndex,
            "UNIQUE",
            "Remediate data before enforcing the unique index.",
            ChangeRisk.Moderate("Remediate duplicates before enforcing unique index."),
            ImmutableArray.Create(
                "Unique duplicates=True (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)"),
            index: new IndexCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ABC_ORDER"), new IndexName("IX_OSUSR_ABC_ORDER_OrderNumber")),
            disposition: Opportunities.OpportunityDisposition.NeedsRemediation,
            statements: ImmutableArray.Create(
                "CREATE UNIQUE INDEX [IX_OSUSR_ABC_ORDER_OrderNumber] ON [dbo].[OSUSR_ABC_ORDER] ([OrderNumber]);"),
            rationales: ImmutableArray.Create("Duplicate values detected."),
            evidenceSummary: new Opportunities.OpportunityEvidenceSummary(true, true, false, true, null),
            columns: ImmutableArray.Create(
                new Opportunities.OpportunityColumn(
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
                    null)),
            schema: "dbo",
            table: "OSUSR_ABC_ORDER",
            constraintName: "IX_OSUSR_ABC_ORDER_OrderNumber");

        var dispositionCounts = ImmutableDictionary.CreateBuilder<Opportunities.OpportunityDisposition, int>();
        dispositionCounts[Opportunities.OpportunityDisposition.ReadyToApply] = 1;
        dispositionCounts[Opportunities.OpportunityDisposition.NeedsRemediation] = 1;

        var typeCounts = ImmutableDictionary.CreateBuilder<Opportunities.OpportunityType, int>();
        typeCounts[Opportunities.OpportunityType.Nullability] = 1;
        typeCounts[Opportunities.OpportunityType.UniqueIndex] = 1;

        var riskCounts = ImmutableDictionary.CreateBuilder<RiskLevel, int>();
        riskCounts[RiskLevel.Low] = 1;
        riskCounts[RiskLevel.Moderate] = 1;

        return new OpportunitiesReport(
            ImmutableArray.Create(notNullOpportunity, uniqueOpportunity),
            dispositionCounts.ToImmutable(),
            typeCounts.ToImmutable(),
            riskCounts.ToImmutable(),
            capture);
}

    private static string NormalizeEvidenceSections(string script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var lines = script.Split('\n');
        var builder = new List<string>(lines.Length);
        var evidenceBuffer = new List<string>();

        void FlushEvidence()
        {
            if (evidenceBuffer.Count == 0)
            {
                return;
            }

            evidenceBuffer.Sort(StringComparer.Ordinal);
            builder.AddRange(evidenceBuffer);
            evidenceBuffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("-- Evidence:", StringComparison.Ordinal))
            {
                evidenceBuffer.Add(line);
            }
            else
            {
                FlushEvidence();
                builder.Add(line);
            }
        }

        FlushEvidence();
        return string.Join("\n", builder);
    }
}
