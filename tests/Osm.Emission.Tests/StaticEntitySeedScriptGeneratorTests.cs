using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;

namespace Osm.Emission.Tests;

public class StaticEntitySeedScriptGeneratorTests
{
    [Fact]
    public void Generate_ProducesMergeBlocksForEachRow()
    {
        var template = StaticEntitySeedTemplate.Load();
        var definition = new StaticEntitySeedTableDefinition(
            Module: "TestModule",
            LogicalName: "Status",
            Schema: "dbo",
            PhysicalName: "OSUSR_TEST_STATUS",
            EffectiveName: "OSUSR_TEST_STATUS",
            Columns: ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("Name", "NAME", "Text", 50, null, null, IsPrimaryKey: false, IsIdentity: false),
                new StaticEntitySeedColumn("IsActive", "ISACTIVE", "Boolean", null, null, null, IsPrimaryKey: false, IsIdentity: false)));

        var rows = ImmutableArray.Create(
            StaticEntityRow.Create(new object?[] { 1, "Active", true }),
            StaticEntityRow.Create(new object?[] { 2, "Inactive", false }));

        var data = ImmutableArray.Create(StaticEntityTableData.Create(definition, rows));
        var generator = new StaticEntitySeedScriptGenerator();

        var script = generator.Generate(template, data, StaticSeedSynchronizationMode.NonDestructive);

        Assert.Contains("MERGE INTO [dbo].[OSUSR_TEST_STATUS] AS Target", script, StringComparison.Ordinal);
        Assert.Contains("VALUES\n        (1, N'Active', 1),\n        (2, N'Inactive', 0)", script, StringComparison.Ordinal);
        Assert.Contains("Target.[NAME] = Source.[NAME]", script, StringComparison.Ordinal);
        Assert.Contains("VALUES (Source.[ID], Source.[NAME], Source.[ISACTIVE])", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithAuthoritativeMode_AppendsDeleteClause()
    {
        var template = StaticEntitySeedTemplate.Load();
        var definition = new StaticEntitySeedTableDefinition(
            Module: "TestModule",
            LogicalName: "Status",
            Schema: "dbo",
            PhysicalName: "OSUSR_TEST_STATUS",
            EffectiveName: "OSUSR_TEST_STATUS",
            Columns: ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("Name", "NAME", "Text", 50, null, null, IsPrimaryKey: false, IsIdentity: false)));

        var rows = ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "Active" }));
        var data = ImmutableArray.Create(StaticEntityTableData.Create(definition, rows));
        var generator = new StaticEntitySeedScriptGenerator();

        var script = generator.Generate(template, data, StaticSeedSynchronizationMode.Authoritative);

        Assert.Contains("WHEN NOT MATCHED BY SOURCE THEN DELETE;", script, StringComparison.Ordinal);
    }
}
