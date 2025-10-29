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
            PhysicalName: "NAME",
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
        Assert.Equal(
            """
            EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Contains order records',
                @level0type=N'SCHEMA',@level0name=N'dbo',
                @level1type=N'TABLE',@level1name=N'Order';
            """.Trim(),
            scripts[0]);
        Assert.Equal(
            """
            EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Display name',
                @level0type=N'SCHEMA',@level0name=N'dbo',
                @level1type=N'TABLE',@level1name=N'Order',
                @level2type=N'COLUMN',@level2name=N'Name';
            """.Trim(),
            scripts[1]);
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
                PhysicalName: "ID",
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

    [Fact]
    public void BuildExtendedPropertyScripts_escapes_single_quotes_in_descriptions()
    {
        var column = new SmoColumnDefinition(
            PhysicalName: "NAME",
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
            Description: "Customer's preferred title [display]",
            DefaultConstraint: null,
            CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty);

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: "Contains customers' orders [deprecated]",
            Columns: ImmutableArray.Create(column),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new ExtendedPropertyScriptBuilder(_formatter);
        var scripts = builder.BuildExtendedPropertyScripts(table, "Order", SmoFormatOptions.Default);

        Assert.Equal(2, scripts.Length);

        var tableScript = scripts[0];
        Assert.Equal(
            """
            EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Contains customers'' orders [deprecated]',
                @level0type=N'SCHEMA',@level0name=N'dbo',
                @level1type=N'TABLE',@level1name=N'Order';
            """.Trim(),
            tableScript);

        var columnScript = scripts[1];
        Assert.Equal(
            """
            EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer''s preferred title [display]',
                @level0type=N'SCHEMA',@level0name=N'dbo',
                @level1type=N'TABLE',@level1name=N'Order',
                @level2type=N'COLUMN',@level2name=N'Name';
            """.Trim(),
            columnScript);
    }

    [Fact]
    public void BuildExtendedPropertyScripts_emits_index_descriptions()
    {
        var primaryIndex = new SmoIndexDefinition(
            Name: "PK_OSUSR_SALES_ORDER",
            IsUnique: true,
            IsPrimaryKey: true,
            IsPlatformAuto: false,
            Description: "Primary key for orders",
            Columns: ImmutableArray.Create(new SmoIndexColumnDefinition("Id", 0, IsIncluded: false, IsDescending: false)),
            Metadata: SmoIndexMetadata.Empty);

        var secondaryIndex = new SmoIndexDefinition(
            Name: "IX_OSUSR_SALES_ORDER_STATUS",
            IsUnique: false,
            IsPrimaryKey: false,
            IsPlatformAuto: false,
            Description: "Lookup by status",
            Columns: ImmutableArray.Create(new SmoIndexColumnDefinition("Status", 0, IsIncluded: false, IsDescending: false)),
            Metadata: SmoIndexMetadata.Empty);

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray<SmoColumnDefinition>.Empty,
            Indexes: ImmutableArray.Create(primaryIndex, secondaryIndex),
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new ExtendedPropertyScriptBuilder(_formatter);
        var scripts = builder.BuildExtendedPropertyScripts(table, "Order", SmoFormatOptions.Default);

        Assert.Equal(2, scripts.Length);
        Assert.Equal(
            """
            EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Primary key for orders',
                @level0type=N'SCHEMA',@level0name=N'dbo',
                @level1type=N'TABLE',@level1name=N'Order',
                @level2type=N'CONSTRAINT',@level2name=N'PK_Order';
            """.Trim(),
            scripts[0]);
        Assert.Equal(
            """
            EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Lookup by status',
                @level0type=N'SCHEMA',@level0name=N'dbo',
                @level1type=N'TABLE',@level1name=N'Order',
                @level2type=N'INDEX',@level2name=N'IX_Order_STATUS';
            """.Trim(),
            scripts[1]);
    }
}
