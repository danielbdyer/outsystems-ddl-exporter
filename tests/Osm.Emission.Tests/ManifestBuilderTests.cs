using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model.Artifacts;
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
        var tables = new List<TableArtifactSnapshot>
        {
            CreateSnapshot(
                module: "SampleModule",
                schema: "dbo",
                table: "Sample",
                logicalName: "Sample",
                manifestPath: "Modules/SampleModule/dbo.Sample.sql"),
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
        var foreignKeys = ImmutableArray.Create(new ForeignKeyDecisionReport(columnCoordinate, true, false, ImmutableArray<string>.Empty));

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
        var columnModules = ImmutableDictionary<string, string>.Empty.Add(columnCoordinate.ToString(), "SampleModule");
        var indexModules = ImmutableDictionary<string, string>.Empty.Add(indexCoordinate.ToString(), "SampleModule");

        return new PolicyDecisionReport(
            columns,
            uniqueIndexes,
            foreignKeys,
            columnRationales,
            uniqueRationales,
            foreignKeyRationales,
            diagnostics,
            moduleRollups,
            ImmutableDictionary<string, ToggleExportValue>.Empty,
            columnModules,
            indexModules,
            toggles);
    }

    private static TableArtifactSnapshot CreateSnapshot(
        string module,
        string schema,
        string table,
        string logicalName,
        string manifestPath)
    {
        var identity = TableArtifactIdentity.Create(module, module, schema, table, logicalName, null);
        var metadata = TableArtifactMetadata.Create(null);
        var snapshot = TableArtifactSnapshot.Create(
            identity,
            Array.Empty<TableColumnSnapshot>(),
            Array.Empty<TableIndexSnapshot>(),
            Array.Empty<TableForeignKeySnapshot>(),
            Array.Empty<TableTriggerSnapshot>(),
            metadata);

        var emission = TableArtifactEmissionMetadata.Create(
            table,
            manifestPath,
            Array.Empty<string>(),
            Array.Empty<string>(),
            includesExtendedProperties: false);

        return snapshot.WithEmission(emission);
    }
}
