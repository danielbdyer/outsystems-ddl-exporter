using System.Collections.Immutable;
using System.Linq;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Xunit;

namespace Osm.Emission.Tests;

public sealed class DynamicEntityInsertGeneratorTests
{
    private static readonly SqlLiteralFormatter Formatter = new();

    [Fact]
    public void GenerateScripts_DeduplicatesStaticSeedRowsAndSorts()
    {
        var definition = CreateDefinition("App", "dbo", "ENTITIES", "Entities", isIdentity: false);
        var datasetRows = ImmutableArray.Create(
            StaticEntityRow.Create(new object?[] { 2, "Beta" }),
            StaticEntityRow.Create(new object?[] { 3, "Gamma" }),
            StaticEntityRow.Create(new object?[] { 1, "Alpha" }),
            StaticEntityRow.Create(new object?[] { 2, "Beta" }));
        var dataset = new DynamicEntityDataset(ImmutableArray.Create(new StaticEntityTableData(definition, datasetRows)));
        var staticSeeds = ImmutableArray.Create(new StaticEntityTableData(
            definition,
            ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "Alpha" }))));

        var generator = new DynamicEntityInsertGenerator(Formatter);

        var scripts = generator.GenerateScripts(dataset, staticSeeds);

        Assert.Single(scripts);
        var script = scripts[0];
        Assert.Contains("INSERT INTO [dbo].[Entities] WITH (TABLOCK, CHECK_CONSTRAINTS)", script.Script, System.StringComparison.Ordinal);
        var firstIndex = script.Script.IndexOf("(2, N'Beta')", System.StringComparison.Ordinal);
        var secondIndex = script.Script.IndexOf("(3, N'Gamma')", System.StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, "Expected first batch row for key 2.");
        Assert.True(secondIndex > firstIndex, "Expected rows ordered by primary key after deduplication.");
        Assert.DoesNotContain("Alpha", script.Script);
    }

    [Fact]
    public void GenerateScripts_RespectsBatchSize()
    {
        var definition = CreateDefinition("App", "dbo", "BULK", "BulkEntities", isIdentity: true);
        var rows = Enumerable.Range(1, 12)
            .Select(i => StaticEntityRow.Create(new object?[] { i, $"Name-{i}" }))
            .ToImmutableArray();
        var dataset = new DynamicEntityDataset(ImmutableArray.Create(new StaticEntityTableData(definition, rows)));

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var options = new DynamicEntityInsertGenerationOptions(batchSize: 5);

        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty, options);

        Assert.Single(scripts);
        var script = scripts[0].Script;
        // Expect three INSERT batches due to batch size (5 + 5 + 2)
        var insertCount = script.Split("INSERT INTO", System.StringSplitOptions.RemoveEmptyEntries).Length - 1;
        Assert.Equal(3, insertCount);
        Assert.Contains("SET IDENTITY_INSERT [dbo].[BulkEntities] ON", script, System.StringComparison.Ordinal);
        Assert.Contains("SET IDENTITY_INSERT [dbo].[BulkEntities] OFF", script, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateScripts_SortsTablesByModuleAndName()
    {
        var alpha = CreateDefinition("Alpha", "dbo", "ONE", "One", isIdentity: false);
        var beta = CreateDefinition("Beta", "dbo", "TWO", "Two", isIdentity: false);
        var dataset = new DynamicEntityDataset(ImmutableArray.Create(
            new StaticEntityTableData(beta, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "B" }))),
            new StaticEntityTableData(alpha, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "A" })))));

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty);

        Assert.Equal(2, scripts.Length);
        Assert.Equal("Alpha", scripts[0].Definition.Module);
        Assert.Equal("Beta", scripts[1].Definition.Module);
    }

    private static StaticEntitySeedTableDefinition CreateDefinition(
        string module,
        string schema,
        string physicalName,
        string logicalName,
        bool isIdentity)
    {
        var columns = ImmutableArray.Create(
            new StaticEntitySeedColumn(
                LogicalName: "Id",
                ColumnName: "Id",
                EmissionName: "Id",
                DataType: "int",
                Length: null,
                Precision: null,
                Scale: null,
                IsPrimaryKey: true,
                IsIdentity: isIdentity),
            new StaticEntitySeedColumn(
                LogicalName: "Name",
                ColumnName: "Name",
                EmissionName: "Name",
                DataType: "nvarchar",
                Length: 50,
                Precision: null,
                Scale: null,
                IsPrimaryKey: false,
                IsIdentity: false));

        return new StaticEntitySeedTableDefinition(
            module,
            schema,
            physicalName,
            logicalName,
            logicalName,
            columns);
    }
}
