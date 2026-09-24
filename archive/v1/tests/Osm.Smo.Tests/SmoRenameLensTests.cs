using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class SmoRenameLensTests
{
    [Fact]
    public void Project_applies_naming_overrides()
    {
        var table = new SmoTableDefinition(
            Module: "AppCore",
            OriginalModule: "AppCore",
            Name: "OSUSR_ABC_CUSTOMER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Customer",
            Description: null,
            Columns: ImmutableArray<SmoColumnDefinition>.Empty,
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var tables = ImmutableArray.Create(table);
        var snapshots = tables.Select(static t => t.ToSnapshot()).ToImmutableArray();
        var model = SmoModel.Create(tables, snapshots);
        var overrideRule = NamingOverrideRule.Create("dbo", "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_PORTAL").Value;
        var overrides = NamingOverrideOptions.Create(new[] { overrideRule }).Value;

        var lens = new SmoRenameLens();
        var mappings = lens.Project(new SmoRenameLensRequest(model, overrides));

        Assert.Single(mappings);
        var mapping = mappings[0];
        Assert.Equal("AppCore", mapping.Module);
        Assert.Equal("AppCore", mapping.OriginalModule);
        Assert.Equal("dbo", mapping.Schema);
        Assert.Equal("OSUSR_ABC_CUSTOMER", mapping.PhysicalName);
        Assert.Equal("Customer", mapping.LogicalName);
        Assert.Equal("CUSTOMER_PORTAL", mapping.EffectiveName);
    }
}
