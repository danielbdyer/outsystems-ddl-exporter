using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Cli.Commands;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class PolicyCommandFactoryTests
{
    [Fact]
    public async Task Explain_PrintsTableSummary()
    {
        using var workspace = new TempDirectory();
        var report = CreateReport();
        var reportPath = Path.Combine(workspace.Path, "policy-decisions.report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, PolicyDecisionReportJson.GetSerializerOptions()));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "report.html"), "<html></html>");

        var factory = new PolicyCommandFactory();
        var policy = factory.Create();
        var root = new RootCommand { policy };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var exitCode = await parser.InvokeAsync($"policy explain --report {reportPath}", console);

        Assert.Equal(0, exitCode);
        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("Column decisions:", output);
        Assert.Contains("AppCore", output);
        var columnAnchor = PolicyDecisionLinkBuilder.CreateColumnAnchor(report.Columns[0].Column);
        Assert.Contains(PolicyDecisionLinkBuilder.BuildReportLink("report.html", columnAnchor), output);
    }

    [Fact]
    public async Task Explain_FiltersByModuleAndRationale()
    {
        using var workspace = new TempDirectory();
        var report = CreateReport();
        var reportPath = Path.Combine(workspace.Path, "policy-decisions.report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, PolicyDecisionReportJson.GetSerializerOptions()));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "report.html"), "<html></html>");

        var factory = new PolicyCommandFactory();
        var policy = factory.Create();
        var root = new RootCommand { policy };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var exitCode = await parser.InvokeAsync($"policy explain --report {reportPath} --module AppCore --rationale UNIQUE_NO_NULLS", console);

        Assert.Equal(0, exitCode);
        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("UNIQUE_NO_NULLS", output);
        Assert.DoesNotContain("DELETE_RULE_IGNORE", output);
    }

    [Fact]
    public async Task Explain_JsonOutput_ContainsFilteredData()
    {
        using var workspace = new TempDirectory();
        var report = CreateReport();
        var reportPath = Path.Combine(workspace.Path, "policy-decisions.report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, PolicyDecisionReportJson.GetSerializerOptions()));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "report.html"), "<html></html>");

        var factory = new PolicyCommandFactory();
        var policy = factory.Create();
        var root = new RootCommand { policy };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var exitCode = await parser.InvokeAsync($"policy explain --report {reportPath} --format json --severity Warning", console);

        Assert.Equal(0, exitCode);
        var output = console.Out.ToString() ?? string.Empty;
        var jsonText = output.Substring(output.IndexOf('{'));
        using var document = JsonDocument.Parse(jsonText);
        var rootElement = document.RootElement;
        var columns = rootElement.GetProperty("columns");
        Assert.Equal(1, columns.GetArrayLength());
        var diagnostics = rootElement.GetProperty("diagnostics");
        Assert.Equal(1, diagnostics.GetArrayLength());
        Assert.Equal("Warning", diagnostics[0].GetProperty("severity").GetString());
    }

    private static PolicyDecisionReport CreateReport()
    {
        var column = new ColumnDecisionReport(
            new ColumnCoordinate(new SchemaName("dbo"), new TableName("Customer"), new ColumnName("Email")),
            true,
            false,
            ImmutableArray.Create("UNIQUE_NO_NULLS", "MANDATORY"))
        {
            Module = "AppCore"
        };

        var unique = new UniqueIndexDecisionReport(
            new IndexCoordinate(new SchemaName("dbo"), new TableName("Customer"), new IndexName("IX_Customer_Email")),
            true,
            false,
            ImmutableArray.Create("UNIQUE_NO_NULLS"))
        {
            Module = "AppCore"
        };

        var foreignKey = new ForeignKeyDecisionReport(
            new ColumnCoordinate(new SchemaName("dbo"), new TableName("Customer"), new ColumnName("RegionId")),
            false,
            ImmutableArray.Create("DELETE_RULE_IGNORE"))
        {
            Module = "AppCore"
        };

        var diagnostic = new TighteningDiagnostic(
            Code: "tightening.entity.duplicate",
            Message: "Duplicate logical name",
            Severity: TighteningDiagnosticSeverity.Warning,
            LogicalName: "Customer",
            CanonicalModule: "AppCore",
            CanonicalSchema: "dbo",
            CanonicalPhysicalName: "OSUSR_ABC_CUSTOMER",
            Candidates: ImmutableArray<TighteningDuplicateCandidate>.Empty,
            ResolvedByOverride: false);

        var rollup = new ModuleDecisionRollup(
            ColumnCount: 1,
            TightenedColumnCount: 1,
            RemediationColumnCount: 0,
            UniqueIndexCount: 1,
            UniqueIndexesEnforcedCount: 1,
            UniqueIndexesRequireRemediationCount: 0,
            ForeignKeyCount: 1,
            ForeignKeysCreatedCount: 0,
            ColumnRationales: ImmutableDictionary<string, int>.Empty.Add("UNIQUE_NO_NULLS", 1),
            UniqueIndexRationales: ImmutableDictionary<string, int>.Empty.Add("UNIQUE_NO_NULLS", 1),
            ForeignKeyRationales: ImmutableDictionary<string, int>.Empty.Add("DELETE_RULE_IGNORE", 1));

        return new PolicyDecisionReport(
            ImmutableArray.Create(column),
            ImmutableArray.Create(unique),
            ImmutableArray.Create(foreignKey),
            ImmutableDictionary<string, int>.Empty.Add("UNIQUE_NO_NULLS", 1),
            ImmutableDictionary<string, int>.Empty.Add("UNIQUE_NO_NULLS", 1),
            ImmutableDictionary<string, int>.Empty.Add("DELETE_RULE_IGNORE", 1),
            ImmutableArray.Create(diagnostic),
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty.Add("AppCore", rollup),
            ImmutableDictionary<string, ToggleExportValue>.Empty.Add(
                TighteningToggleKeys.PolicyMode,
                new ToggleExportValue(TighteningMode.EvidenceGated, ToggleSource.Default)),
            TighteningToggleSnapshot.Create(TighteningOptions.Default));
    }
}
