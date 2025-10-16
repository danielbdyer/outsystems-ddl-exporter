using System.Collections.Immutable;
using System.Text.Json;
using Osm.Cli;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Cli.Tests;

public class ProfileSnapshotDebugFormatterTests
{
    [Fact]
    public void ToJson_ProducesIndentedSnapshot()
    {
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("Customer").Value;
        var column = ColumnName.Create("Id").Value;
        var tenantColumn = ColumnName.Create("TenantId").Value;

        var columnProfile = ColumnProfile.Create(
            schema,
            table,
            column,
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: true,
            isUniqueKey: true,
            defaultDefinition: "((0))",
            rowCount: 10,
            nullCount: 0).Value;

        var uniqueProfile = UniqueCandidateProfile.Create(
            schema,
            table,
            column,
            hasDuplicate: false).Value;

        var compositeProfile = CompositeUniqueCandidateProfile.Create(
            schema,
            table,
            new[] { column, tenantColumn },
            hasDuplicate: false).Value;

        var foreignKeyReference = ForeignKeyReference.Create(
            schema,
            table,
            tenantColumn,
            SchemaName.Create("dbo").Value,
            TableName.Create("Tenant").Value,
            ColumnName.Create("Id").Value,
            hasDatabaseConstraint: true).Value;

        var foreignKeyReality = ForeignKeyReality.Create(
            foreignKeyReference,
            hasOrphan: false,
            isNoCheck: true).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { columnProfile },
            new[] { uniqueProfile },
            new[] { compositeProfile },
            new[] { foreignKeyReality }).Value;

        var json = ProfileSnapshotDebugFormatter.ToJson(snapshot);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("Columns", out var columns));
        Assert.Equal("dbo", columns[0].GetProperty("Schema").GetString());
        Assert.Equal("Customer", columns[0].GetProperty("Table").GetString());
        Assert.Equal("Id", columns[0].GetProperty("Column").GetString());
        Assert.Equal(10, columns[0].GetProperty("RowCount").GetInt64());

        var uniqueCandidates = root.GetProperty("UniqueCandidates");
        Assert.Equal("Id", uniqueCandidates[0].GetProperty("Column").GetString());

        Assert.Equal("TenantId", root
            .GetProperty("CompositeUniqueCandidates")[0]
            .GetProperty("Columns")[1]
            .GetString());

        var foreignKeys = root.GetProperty("ForeignKeys");
        Assert.Equal("Tenant", foreignKeys[0].GetProperty("ToTable").GetString());
        Assert.True(foreignKeys[0].GetProperty("HasDatabaseConstraint").GetBoolean());
        Assert.True(foreignKeys[0].GetProperty("IsNoCheck").GetBoolean());
    }

    [Fact]
    public void ToSummaryLines_FormatsInsightReport()
    {
        var insights = ImmutableArray.Create(
            new ProfileInsight(ProfileInsightSeverity.Info, "Profile scanned 42 row(s) across 5 column(s)."),
            new ProfileInsight(ProfileInsightSeverity.Warning, "Foreign key CustomerId -> dbo.Customer.Id is not constrained in the database; tightening will create it."));
        var module = new ProfileInsightModule("dbo", "Orders", insights);
        var report = new ProfileInsightReport(ImmutableArray.Create(module));

        var lines = ProfileSnapshotDebugFormatter.ToSummaryLines(report);

        Assert.Equal(3, lines.Length);
        Assert.Equal("dbo.Orders:", lines[0]);
        Assert.Equal("  [info] Profile scanned 42 row(s) across 5 column(s).", lines[1]);
        Assert.Equal(
            "  [warning] Foreign key CustomerId -> dbo.Customer.Id is not constrained in the database; tightening will create it.",
            lines[2]);
    }
}
