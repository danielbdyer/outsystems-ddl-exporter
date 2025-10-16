using System.Linq;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Tests;

public class ProfileInsightAnalyzerTests
{
    [Fact]
    public void Analyze_ReturnsEmptyReportForEmptySnapshot()
    {
        var snapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var analyzer = new ProfileInsightAnalyzer();
        var report = analyzer.Analyze(snapshot);

        Assert.Same(ProfileInsightReport.Empty, report);
    }

    [Fact]
    public void Analyze_GroupsInsightsByTable()
    {
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("Orders").Value;
        var columnName = ColumnName.Create("CustomerId").Value;
        var optionalColumn = ColumnName.Create("Notes").Value;

        var columnProfiles = new[]
        {
            ColumnProfile.Create(
                schema,
                table,
                columnName,
                isNullablePhysical: false,
                isComputed: false,
                isPrimaryKey: false,
                isUniqueKey: false,
                defaultDefinition: null,
                rowCount: 100,
                nullCount: 3).Value,
            ColumnProfile.Create(
                schema,
                table,
                optionalColumn,
                isNullablePhysical: true,
                isComputed: true,
                isPrimaryKey: false,
                isUniqueKey: false,
                defaultDefinition: null,
                rowCount: 100,
                nullCount: 0).Value,
        };

        var uniqueProfiles = new[]
        {
            UniqueCandidateProfile.Create(schema, table, columnName, hasDuplicate: true).Value,
        };

        var compositeProfiles = new[]
        {
            CompositeUniqueCandidateProfile.Create(schema, table, new[] { columnName, optionalColumn }, hasDuplicate: true).Value,
        };

        var foreignKeyReference = ForeignKeyReference.Create(
            schema,
            table,
            columnName,
            schema,
            TableName.Create("Customers").Value,
            ColumnName.Create("Id").Value,
            hasDatabaseConstraint: false).Value;

        var foreignKeys = new[]
        {
            ForeignKeyReality.Create(foreignKeyReference, hasOrphan: true, isNoCheck: true).Value,
        };

        var snapshot = ProfileSnapshot.Create(columnProfiles, uniqueProfiles, compositeProfiles, foreignKeys).Value;
        var analyzer = new ProfileInsightAnalyzer();

        var report = analyzer.Analyze(snapshot);

        Assert.NotNull(report);
        var module = Assert.Single(report.Modules);
        Assert.Equal("dbo", module.Schema);
        Assert.Equal("Orders", module.Table);

        Assert.Contains(module.Insights, insight =>
            insight.Severity == ProfileInsightSeverity.Info
            && insight.Message.Contains("100", StringComparison.Ordinal));

        Assert.Contains(module.Insights, insight =>
            insight.Severity == ProfileInsightSeverity.Critical
            && insight.Message.Contains("CustomerId", StringComparison.Ordinal)
            && insight.Message.Contains("null", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(module.Insights, insight =>
            insight.Severity == ProfileInsightSeverity.Critical
            && insight.Message.Contains("orphaned", StringComparison.OrdinalIgnoreCase));

        var warningMessages = module.Insights.Where(insight => insight.Severity == ProfileInsightSeverity.Warning)
            .Select(insight => insight.Message)
            .ToArray();

        Assert.Contains(warningMessages, message => message.Contains("not constrained", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warningMessages, message => message.Contains("WITH NOCHECK", StringComparison.OrdinalIgnoreCase));
    }
}
