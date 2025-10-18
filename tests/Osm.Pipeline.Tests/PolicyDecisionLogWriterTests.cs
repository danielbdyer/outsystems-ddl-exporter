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
            ImmutableArray.Create("profile-clean"));

        var uniqueDecision = UniqueIndexDecision.Create(
            indexCoordinate,
            enforceUnique: true,
            requiresRemediation: false,
            ImmutableArray<string>.Empty);

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty.Add(columnCoordinate, nullabilityDecision),
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty.Add(columnCoordinate, foreignKeyDecision),
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty.Add(indexCoordinate, uniqueDecision),
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty.Add(columnCoordinate, "Accounting"),
            ImmutableDictionary<IndexCoordinate, string>.Empty.Add(indexCoordinate, "Accounting"),
            TighteningOptions.Default);

        var report = PolicyDecisionReporter.Create(decisions);

        using var output = new TempDirectory();
        var writer = new PolicyDecisionLogWriter();
        var path = await writer.WriteAsync(output.Path, report, CancellationToken.None);

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
    }
}
