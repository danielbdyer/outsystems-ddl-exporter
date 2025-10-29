using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Emission.Tests;

public class ManifestBuilderTests
{
    [Fact]
    public void Build_populates_summary_and_defaults_when_report_is_provided()
    {
        var builder = new ManifestBuilder();
        var tables = new List<TableManifestEntry>
        {
            new(
                Module: "SampleModule",
                Schema: "dbo",
                Table: "Sample",
                TableFile: "Modules/SampleModule/dbo.Sample.sql",
                Indexes: ImmutableArray<string>.Empty,
                ForeignKeys: ImmutableArray<string>.Empty,
                IncludesExtendedProperties: false),
        };

        var options = SmoBuildOptions.Default with
        {
            IncludePlatformAutoIndexes = true,
            EmitBareTableOnly = true,
            ModuleParallelism = 4,
        };

        var metadata = new SsdtEmissionMetadata("SHA256", "hash");
        var report = CreateDecisionReport();

        var manifest = builder.Build(
            tables,
            options,
            metadata,
            report,
            preRemediation: null,
            coverage: null,
            predicateCoverage: null,
            unsupported: null,
            tableCount: 1,
            columnCount: 2,
            constraintCount: 1);

        Assert.NotNull(manifest.PolicySummary);
        Assert.Equal(report.ColumnCount, manifest.PolicySummary!.ColumnCount);
        Assert.True(manifest.PolicySummary.ForeignKeysCreatedCount > 0);
        Assert.Equal(options.ModuleParallelism, manifest.Options.ModuleParallelism);
        Assert.True(manifest.Coverage.Tables.Percentage > 0);
        Assert.Equal(SsdtPredicateCoverage.Empty, manifest.PredicateCoverage);
    }

    private static PolicyDecisionReport CreateDecisionReport()
    {
        var columnCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("Id"));
        var indexCoordinate = new IndexCoordinate(new SchemaName("dbo"), new TableName("Orders"), new IndexName("IX_Orders"));

        var columns = ImmutableArray.Create(new ColumnDecisionReport(columnCoordinate, true, false, ImmutableArray<string>.Empty));
        var uniqueIndexes = ImmutableArray.Create(new UniqueIndexDecisionReport(indexCoordinate, true, false, ImmutableArray<string>.Empty));
        var foreignKeys = ImmutableArray.Create(new ForeignKeyDecisionReport(columnCoordinate, true, ImmutableArray<string>.Empty));

        var columnRationales = ImmutableDictionary<string, int>.Empty.Add("Evidence", 1);
        var uniqueRationales = ImmutableDictionary<string, int>.Empty;
        var foreignKeyRationales = ImmutableDictionary<string, int>.Empty;
        var diagnostics = ImmutableArray<TighteningDiagnostic>.Empty;
        var moduleRollups = ImmutableDictionary<string, ModuleDecisionRollup>.Empty.Add(
            "SampleModule",
            new ModuleDecisionRollup(
                ColumnCount: 1,
                TightenedColumnCount: 1,
                RemediationColumnCount: 0,
                UniqueIndexCount: 1,
                UniqueIndexesEnforcedCount: 1,
                UniqueIndexesRequireRemediationCount: 0,
                ForeignKeyCount: 1,
                ForeignKeysCreatedCount: 1,
                ColumnRationales: ImmutableDictionary<string, int>.Empty,
                UniqueIndexRationales: ImmutableDictionary<string, int>.Empty,
                ForeignKeyRationales: ImmutableDictionary<string, int>.Empty));

        var toggles = TighteningToggleSnapshot.Create(TighteningOptions.Default);

        return new PolicyDecisionReport(
            columns,
            uniqueIndexes,
            foreignKeys,
            columnRationales,
            uniqueRationales,
            foreignKeyRationales,
            diagnostics,
            moduleRollups,
            toggles);
    }
}
