using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Cli.Commands;
using Osm.Validation.Tightening;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class PolicyCommandFactoryTests
{
    [Fact]
    public async Task ExplainCommand_WritesTableOutputWithFilters()
    {
        using var workspace = new TempDirectory();
        var report = CreateReport();
        var reportPath = Path.Combine(workspace.Path, "policy-decision-report.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "report.html"), "<html></html>");

        var factory = new PolicyCommandFactory();
        var command = factory.Create();
        var root = new RootCommand { command };
        var console = new TestConsole();

        var args = new[] { "policy", "explain", "--path", reportPath, "--rationale", "DATA_HAS_ORPHANS" };
        var exitCode = await root.InvokeAsync(args, console);

        Assert.Equal(0, exitCode);
        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("Policy decision report:", output);
        Assert.Contains("Foreign Key", output);
        Assert.Contains("DATA_HAS_ORPHANS", output);
        Assert.Contains("report.html#foreign-key-sales-orders-regionid", output);
        Assert.Contains("NOT NULL Confirmed", output);
    }

    [Fact]
    public async Task ExplainCommand_EmitsJsonWhenRequested()
    {
        using var workspace = new TempDirectory();
        var report = CreateReport();
        var reportPath = Path.Combine(workspace.Path, "policy-decision-report.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, options));

        var factory = new PolicyCommandFactory();
        var command = factory.Create();
        var root = new RootCommand { command };
        var console = new TestConsole();

        var args = new[] { "policy", "explain", "--path", reportPath, "--format", "json", "--severity", "warning" };
        var exitCode = await root.InvokeAsync(args, console);

        Assert.Equal(0, exitCode);
        var output = console.Out.ToString() ?? string.Empty;
        using var document = JsonDocument.Parse(output);
        var rootElement = document.RootElement;
        Assert.Equal(reportPath, rootElement.GetProperty("reportPath").GetString());
        var filters = rootElement.GetProperty("filters");
        Assert.Contains(
            filters.GetProperty("severities").EnumerateArray(),
            element => string.Equals(element.GetString(), "Warning", StringComparison.OrdinalIgnoreCase));
        var modules = rootElement.GetProperty("modules");
        Assert.Equal(1, modules.GetArrayLength());
        var diagnostics = rootElement.GetProperty("diagnostics");
        Assert.Equal(1, diagnostics.GetArrayLength());
    }

    private static PolicyDecisionReport CreateReport()
    {
        var columnCoordinate = new ColumnCoordinate(
            new SchemaName("Sales"),
            new TableName("Orders"),
            new ColumnName("CustomerId"));
        var uniqueCoordinate = new IndexCoordinate(
            new SchemaName("Sales"),
            new TableName("Orders"),
            new IndexName("IX_Orders_Unique"));
        var foreignCoordinate = new ColumnCoordinate(
            new SchemaName("Sales"),
            new TableName("Orders"),
            new ColumnName("RegionId"));

        var columns = ImmutableArray.Create(
            new ColumnDecisionReport(
                columnCoordinate,
                true,
                false,
                ImmutableArray.Create("MANDATORY")));

        var uniqueIndexes = ImmutableArray.Create(
            new UniqueIndexDecisionReport(
                uniqueCoordinate,
                true,
                false,
                ImmutableArray.Create("UNIQUE_NO_NULLS")));

        var foreignKeys = ImmutableArray.Create(
            new ForeignKeyDecisionReport(
                foreignCoordinate,
                false,
                ImmutableArray.Create("DATA_HAS_ORPHANS")));

        var diagnostics = ImmutableArray.Create(
            new TighteningDiagnostic(
                "tightening.entity.duplicate",
                "Duplicate logical name detected.",
                TighteningDiagnosticSeverity.Warning,
                "Customer",
                "Sales",
                "Sales",
                "Orders",
                ImmutableArray<TighteningDuplicateCandidate>.Empty,
                false));

        var moduleRollups = ImmutableDictionary<string, ModuleDecisionRollup>.Empty.Add(
            "Sales",
            new ModuleDecisionRollup(
                1,
                1,
                0,
                1,
                1,
                0,
                1,
                0,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty));

        var togglePrecedence = ImmutableDictionary<string, ToggleExportValue>.Empty.Add(
            TighteningToggleKeys.PolicyMode,
            new ToggleExportValue(TighteningMode.EvidenceGated, ToggleSource.CommandLine));

        var columnModules = ImmutableDictionary<string, string>.Empty
            .Add(columnCoordinate.ToString(), "Sales")
            .Add(foreignCoordinate.ToString(), "Sales");
        var indexModules = ImmutableDictionary<string, string>.Empty
            .Add(uniqueCoordinate.ToString(), "Sales");

        return new PolicyDecisionReport(
            columns,
            uniqueIndexes,
            foreignKeys,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            diagnostics,
            moduleRollups,
            togglePrecedence,
            columnModules,
            indexModules,
            TighteningToggleSnapshot.Create(TighteningOptions.Default));
    }
}
