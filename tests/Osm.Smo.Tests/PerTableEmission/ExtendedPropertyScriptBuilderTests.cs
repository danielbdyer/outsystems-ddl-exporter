using System.Collections.Immutable;
using Microsoft.SqlServer.Management.Smo;
using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class ExtendedPropertyScriptBuilderTests
{
    private readonly SqlScriptFormatter _formatter = new();

    [Fact]
    public void BuildExtendedPropertyScripts_emits_table_and_column_descriptions()
    {
        var column = new SmoColumnDefinition(
            Name: "Name",
            LogicalName: "Name",
            DataType: DataType.NVarChar(50),
            Nullable: true,
            IsIdentity: false,
            IdentitySeed: 0,
            IdentityIncrement: 0,
            IsComputed: false,
            ComputedExpression: null,
            DefaultExpression: null,
            Collation: null,
            Description: "Display name",
            DefaultConstraint: null,
            CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty);

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: "Contains order records",
            Columns: ImmutableArray.Create(column),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new ExtendedPropertyScriptBuilder(_formatter);
        var scripts = builder.BuildExtendedPropertyScripts(table, "Order", SmoFormatOptions.Default);

        Assert.Equal(2, scripts.Length);
        Assert.Contains("EXEC sys.sp_updateextendedproperty", scripts[0]);
        Assert.Contains("Display name", scripts[1]);
    }

    [Fact]
    public void BuildExtendedPropertyScripts_skips_when_no_descriptions()
    {
        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray.Create(new SmoColumnDefinition(
                Name: "Id",
                LogicalName: "Id",
                DataType: DataType.Int,
                Nullable: false,
                IsIdentity: true,
                IdentitySeed: 1,
                IdentityIncrement: 1,
                IsComputed: false,
                ComputedExpression: null,
                DefaultExpression: null,
                Collation: null,
                Description: null,
                DefaultConstraint: null,
                CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty)),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new ExtendedPropertyScriptBuilder(_formatter);
        var scripts = builder.BuildExtendedPropertyScripts(table, "Order", SmoFormatOptions.Default);

        Assert.True(scripts.IsDefaultOrEmpty);
    }
}
