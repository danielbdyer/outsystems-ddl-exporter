using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public class DecisionSummaryFormatterTests
{
    [Fact]
    public void FormatForConsole_WhenMandatoryColumns_ReturnsMandatorySummary()
    {
        var columns = ImmutableArray.Create(
            CreateColumn("dbo", "CUSTOMER", "EMAIL", makeNotNull: true, TighteningRationales.Mandatory, TighteningRationales.DataNoNulls),
            CreateColumn("dbo", "CUSTOMER", "CITYID", makeNotNull: true, TighteningRationales.Mandatory, TighteningRationales.DataNoNulls));

        var report = CreateReport(columns);

        var lines = PolicyDecisionSummaryFormatter.FormatForConsole(report);

        var message = Assert.Single(lines);
        Assert.Equal(
            "2 attributes across 1 entity were tightened to NOT NULL based on logical mandatory metadata after confirming the profiler reported no null rows.",
            message);
    }

    [Fact]
    public void FormatForConsole_WhenProfileMissingColumns_ReturnsNullableSummary()
    {
        var column = CreateColumn(
            "dbo",
            "CUSTOMER",
            "LEGACYCODE",
            makeNotNull: false,
            TighteningRationales.ProfileMissing);

        var report = CreateReport(ImmutableArray.Create(column));

        var lines = PolicyDecisionSummaryFormatter.FormatForConsole(report);

        var message = Assert.Single(lines);
        Assert.Equal(
            "1 attribute across 1 entity stayed nullable because profiling evidence was unavailable.",
            message);
    }

    [Fact]
    public void FormatForConsole_WhenNullContradictions_ReturnsContradictionSummary()
    {
        var column = CreateColumn(
            "dbo",
            "CUSTOMER",
            "EMAIL",
            makeNotNull: false,
            TighteningRationales.Mandatory,
            TighteningRationales.DataHasNulls);

        var report = CreateReport(ImmutableArray.Create(column));

        var lines = PolicyDecisionSummaryFormatter.FormatForConsole(report);

        var message = Assert.Single(lines);
        Assert.Equal(
            "1 attribute across 1 entity stayed nullable because profiling detected NULL values.",
            message);
    }

    [Fact]
    public void FormatForConsole_SortsSummariesByMagnitude()
    {
        var columns = ImmutableArray.Create(
            CreateColumn("dbo", "CUSTOMER", "EMAIL", makeNotNull: true, TighteningRationales.Mandatory, TighteningRationales.DataNoNulls),
            CreateColumn("dbo", "CUSTOMER", "ID", makeNotNull: true, TighteningRationales.PrimaryKey, TighteningRationales.PhysicalNotNull, TighteningRationales.DataNoNulls),
            CreateColumn("dbo", "CUSTOMER", "PHONENUMBER", makeNotNull: true, TighteningRationales.Mandatory, TighteningRationales.DataNoNulls));

        var report = CreateReport(columns);

        var lines = PolicyDecisionSummaryFormatter.FormatForConsole(report);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("2 attributes across 1 entity were tightened to NOT NULL based on logical mandatory metadata", lines[0]);
        Assert.Contains("primary key", lines[1]);
    }

    private static PolicyDecisionReport CreateReport(ImmutableArray<ColumnDecisionReport> columns)
        => new(
            columns,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            ImmutableDictionary<string, ToggleExportValue>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            TighteningToggleSnapshot.Create(TighteningOptions.Default));

    private static ColumnDecisionReport CreateColumn(
        string schema,
        string table,
        string column,
        bool makeNotNull,
        params string[] rationales)
        => new(
            new ColumnCoordinate(new SchemaName(schema), new TableName(table), new ColumnName(column)),
            makeNotNull,
            RequiresRemediation: false,
            rationales.Length == 0 ? ImmutableArray<string>.Empty : ImmutableArray.CreateRange(rationales));
}
