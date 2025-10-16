using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests.Profiling;

public class ProfilingInsightAnalyzerTests
{
    [Fact]
    public void Analyze_ReturnsHighNullWarningForSparseColumn()
    {
        var analyzer = new ProfilingInsightAnalyzer();
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("Customer").Value;
        var column = ColumnName.Create("Email").Value;
        var profile = ColumnProfile.Create(
            schema,
            table,
            column,
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 1_000,
            nullCount: 650).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { profile },
            Enumerable.Empty<UniqueCandidateProfile>(),
            Enumerable.Empty<CompositeUniqueCandidateProfile>(),
            Enumerable.Empty<ForeignKeyReality>()).Value;

        var insights = analyzer.Analyze(snapshot);
        var highNull = Assert.Single(insights.Where(i => i.Code == ProfilingInsightCodes.HighNullDensity));
        Assert.Equal(ProfilingInsightSeverity.Warning, highNull.Severity);
        Assert.Contains("NULL", highNull.Message);
    }

    [Fact]
    public void Analyze_FlagsDuplicateUniqueCandidate()
    {
        var analyzer = new ProfilingInsightAnalyzer();
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("Customer").Value;
        var column = ColumnName.Create("Email").Value;
        var profile = ColumnProfile.Create(
            schema,
            table,
            column,
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 500,
            nullCount: 0).Value;

        var uniqueCandidate = UniqueCandidateProfile.Create(schema, table, column, hasDuplicate: true).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { profile },
            new[] { uniqueCandidate },
            Enumerable.Empty<CompositeUniqueCandidateProfile>(),
            Enumerable.Empty<ForeignKeyReality>()).Value;

        var insights = analyzer.Analyze(snapshot);
        var duplicate = Assert.Single(insights.Where(i => i.Code == ProfilingInsightCodes.UniqueCandidateDuplicates));
        Assert.Equal(ProfilingInsightSeverity.Warning, duplicate.Severity);
    }

    [Fact]
    public void Analyze_SurfacesForeignKeyOpportunityWhenClean()
    {
        var analyzer = new ProfilingInsightAnalyzer();
        var fromSchema = SchemaName.Create("dbo").Value;
        var fromTable = TableName.Create("Invoice").Value;
        var fromColumn = ColumnName.Create("CustomerId").Value;
        var toSchema = SchemaName.Create("dbo").Value;
        var toTable = TableName.Create("Customer").Value;
        var toColumn = ColumnName.Create("Id").Value;

        var reference = ForeignKeyReference.Create(fromSchema, fromTable, fromColumn, toSchema, toTable, toColumn, hasDatabaseConstraint: false).Value;
        var reality = ForeignKeyReality.Create(reference, hasOrphan: false, isNoCheck: false).Value;

        var snapshot = ProfileSnapshot.Create(
            Enumerable.Empty<ColumnProfile>(),
            Enumerable.Empty<UniqueCandidateProfile>(),
            Enumerable.Empty<CompositeUniqueCandidateProfile>(),
            new[] { reality }).Value;

        var insights = analyzer.Analyze(snapshot);
        var opportunity = Assert.Single(insights.Where(i => i.Code == ProfilingInsightCodes.ForeignKeyOpportunity));
        Assert.Equal(ProfilingInsightSeverity.Info, opportunity.Severity);
        Assert.Contains("foreign key", opportunity.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_OrdersCriticalInsightsFirst()
    {
        var analyzer = new ProfilingInsightAnalyzer();
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("Orders").Value;
        var badColumn = ColumnName.Create("Number").Value;
        var warningColumn = ColumnName.Create("OptionalNotes").Value;

        var criticalProfile = ColumnProfile.Create(
            schema,
            table,
            badColumn,
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 100,
            nullCount: 1).Value;

        var warningProfile = ColumnProfile.Create(
            schema,
            table,
            warningColumn,
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 200,
            nullCount: 120).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { criticalProfile, warningProfile },
            Enumerable.Empty<UniqueCandidateProfile>(),
            Enumerable.Empty<CompositeUniqueCandidateProfile>(),
            Enumerable.Empty<ForeignKeyReality>()).Value;

        var insights = analyzer.Analyze(snapshot);

        Assert.True(insights.Length >= 2);
        Assert.Equal(ProfilingInsightSeverity.Critical, insights[0].Severity);
    }
}
