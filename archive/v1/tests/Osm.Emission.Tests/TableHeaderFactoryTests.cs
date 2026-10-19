using System.Collections.Immutable;
using Osm.Emission;
using Osm.Smo;

namespace Osm.Emission.Tests;

public class TableHeaderFactoryTests
{
    [Fact]
    public void Create_returns_null_when_headers_disabled()
    {
        var factory = new TableHeaderFactory();
        var table = CreateTable();
        var options = SmoBuildOptions.Default;
        var renameLookup = ImmutableDictionary<string, SmoRenameMapping>.Empty;

        var result = factory.Create(table, options, renameLookup);

        Assert.Null(result);
    }

    [Fact]
    public void Create_includes_rename_details_when_mapping_is_present()
    {
        var factory = new TableHeaderFactory();
        var table = CreateTable();
        var options = SmoBuildOptions.Default with { Header = PerTableHeaderOptions.EnabledTemplate };
        var rename = ImmutableDictionary<string, SmoRenameMapping>.Empty.Add(
            "dbo.Sample",
            new SmoRenameMapping(
                Module: "SampleModule",
                OriginalModule: "LegacyModule",
                Schema: "dbo",
                PhysicalName: "Sample",
                LogicalName: "Sample",
                EffectiveName: "Renamed"));

        var result = factory.Create(table, options, rename);

        Assert.NotNull(result);
        Assert.Contains(result!, item => item.Label == "RenamedFrom" && item.Value == "dbo.Sample");
        Assert.Contains(result!, item => item.Label == "EffectiveName" && item.Value == "Renamed");
        Assert.Contains(result!, item => item.Label == "OriginalModule" && item.Value == "LegacyModule");
    }

    private static SmoTableDefinition CreateTable()
    {
        return new SmoTableDefinition(
            Module: "SampleModule",
            OriginalModule: "LegacyModule",
            Name: "Sample",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Sample",
            Description: null,
            Columns: ImmutableArray<SmoColumnDefinition>.Empty,
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);
    }
}
