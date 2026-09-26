using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class PolicyDecisionLogWriterTests
{
    [Fact]
    public async Task WriteAsync_EmitsModuleRollupsAndTogglePrecedence()
    {
        var columnCoordinate = new ColumnCoordinate(
            new SchemaName("dbo"),
            new TableName("Customer"),
            new ColumnName("Email"));
        var indexCoordinate = new IndexCoordinate(
            new SchemaName("dbo"),
            new TableName("Customer"),
            new IndexName("IX_Customer_Email"));

        var nullabilityDecision = NullabilityDecision.Create(
            columnCoordinate,
            makeNotNull: true,
            requiresRemediation: false,
            ImmutableArray.Create("unique"));

        var foreignKeyDecision = ForeignKeyDecision.Create(
            columnCoordinate,
            createConstraint: true,
            scriptWithNoCheck: false,
            ImmutableArray.Create("profile-clean"));

        var uniqueDecision = UniqueIndexDecision.Create(
            indexCoordinate,
            enforceUnique: true,
            requiresRemediation: false,
            ImmutableArray<string>.Empty);
        var columnIdentity = new ColumnIdentity(
            columnCoordinate,
            new ModuleName("Accounting"),
            new EntityName("Customer"),
            new TableName("Customer"),
            new AttributeName("Email"));

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty.Add(columnCoordinate, nullabilityDecision),
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty.Add(columnCoordinate, foreignKeyDecision),
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty.Add(indexCoordinate, uniqueDecision),
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty.Add(columnCoordinate, columnIdentity),
            ImmutableDictionary<IndexCoordinate, string>.Empty.Add(indexCoordinate, "Accounting"),
            TighteningOptions.Default);

        var report = PolicyDecisionReporter.Create(decisions);

        using var output = new TempDirectory();
        var writer = new PolicyDecisionLogWriter();
        var result = await writer.WriteAsync(output.Path, report, CancellationToken.None);
        Assert.True(result.IsSuccess);
        var path = result.Value;
        var reportPath = Path.Combine(output.Path, "policy-decision-report.json");

        var json = await File.ReadAllTextAsync(path);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var rollups = root.GetProperty("ModuleRollups");
        var accounting = rollups.GetProperty("Accounting");
        Assert.Equal(1, accounting.GetProperty("ColumnCount").GetInt32());
        Assert.Equal(1, accounting.GetProperty("TightenedColumnCount").GetInt32());
        Assert.Equal(1, accounting.GetProperty("UniqueIndexCount").GetInt32());
        Assert.Equal(1, accounting.GetProperty("ForeignKeyCount").GetInt32());
        var moduleColumnRationales = accounting.GetProperty("ColumnRationales");
        Assert.Equal(1, moduleColumnRationales.GetProperty("unique").GetInt32());
        var moduleForeignKeyRationales = accounting.GetProperty("ForeignKeyRationales");
        Assert.Equal(1, moduleForeignKeyRationales.GetProperty("profile-clean").GetInt32());
        var moduleUniqueRationales = accounting.GetProperty("UniqueIndexRationales");
        Assert.Equal(JsonValueKind.Object, moduleUniqueRationales.ValueKind);

        var toggles = root.GetProperty("TogglePrecedence");
        var mode = toggles.GetProperty(TighteningToggleKeys.PolicyMode);
        var modeValue = mode.GetProperty("Value");
        var actualMode = modeValue.ValueKind == JsonValueKind.String
            ? modeValue.GetString()
            : modeValue.GetRawText();
        Assert.Equal(TighteningMode.EvidenceGated.ToString(), actualMode);
        Assert.Equal((int)ToggleSource.Default, mode.GetProperty("Source").GetInt32());

        var numericSentinel = toggles.GetProperty(TighteningToggleKeys.RemediationSentinelNumeric);
        Assert.Equal("0", numericSentinel.GetProperty("Value").GetString());
        Assert.Equal((int)ToggleSource.Default, numericSentinel.GetProperty("Source").GetInt32());

        var textSentinel = toggles.GetProperty(TighteningToggleKeys.RemediationSentinelText);
        Assert.Equal(string.Empty, textSentinel.GetProperty("Value").GetString());
        Assert.Equal((int)ToggleSource.Default, textSentinel.GetProperty("Source").GetInt32());

        var dateSentinel = toggles.GetProperty(TighteningToggleKeys.RemediationSentinelDate);
        Assert.Equal("1900-01-01", dateSentinel.GetProperty("Value").GetString());
        Assert.Equal((int)ToggleSource.Default, dateSentinel.GetProperty("Source").GetInt32());

        var mockingToggle = toggles.GetProperty(TighteningToggleKeys.MockingUseProfileMockFolder);
        Assert.False(mockingToggle.GetProperty("Value").GetBoolean());
        Assert.Equal((int)ToggleSource.Default, mockingToggle.GetProperty("Source").GetInt32());

        var mockingFolder = toggles.GetProperty(TighteningToggleKeys.MockingProfileMockFolder);
        Assert.Equal(JsonValueKind.Null, mockingFolder.GetProperty("Value").ValueKind);
        Assert.Equal((int)ToggleSource.Default, mockingFolder.GetProperty("Source").GetInt32());

        var columns = root.GetProperty("Columns");
        Assert.Equal("Accounting", columns[0].GetProperty("Module").GetString());
        var uniqueIndexes = root.GetProperty("UniqueIndexes");
        Assert.Equal("Accounting", uniqueIndexes[0].GetProperty("Module").GetString());
        var foreignKeys = root.GetProperty("ForeignKeys");
        Assert.Equal("Accounting", foreignKeys[0].GetProperty("Module").GetString());

        Assert.True(File.Exists(reportPath));
        var rawReportJson = await File.ReadAllTextAsync(reportPath);
        using var rawReportDocument = JsonDocument.Parse(rawReportJson);
        var columnModules = rawReportDocument.RootElement.GetProperty("ColumnModules");
        Assert.True(columnModules.TryGetProperty("dbo.Customer.Email", out _));
    }
}
