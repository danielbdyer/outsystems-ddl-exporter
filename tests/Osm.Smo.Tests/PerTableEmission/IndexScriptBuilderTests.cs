using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class IndexScriptBuilderTests
{
    private readonly SqlScriptFormatter _formatter = new();

    [Fact]
    public void BuildCreateIndexStatement_includes_filter_and_compression_options()
    {
        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray<SmoColumnDefinition>.Empty,
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var metadata = new SmoIndexMetadata(
            IsDisabled: false,
            IsPadded: true,
            FillFactor: 80,
            IgnoreDuplicateKey: true,
            AllowRowLocks: false,
            AllowPageLocks: true,
            StatisticsNoRecompute: true,
            FilterDefinition: "Status = 'A'",
            DataSpace: new SmoIndexDataSpace("FG_DATA", "FILEGROUP"),
            PartitionColumns: ImmutableArray.Create(new SmoIndexPartitionColumn("PartitionCol", 0)),
            DataCompression: ImmutableArray.Create(
                new SmoIndexCompressionSetting(1, "PAGE"),
                new SmoIndexCompressionSetting(2, "PAGE"),
                new SmoIndexCompressionSetting(4, "ROW")));

        var index = new SmoIndexDefinition(
            Name: "IX_OSUSR_SALES_ORDER_STATUS",
            IsUnique: false,
            IsPrimaryKey: false,
            IsPlatformAuto: false,
            Columns: ImmutableArray.Create(
                new SmoIndexColumnDefinition("Status", 0, IsIncluded: false, IsDescending: true),
                new SmoIndexColumnDefinition("AuditId", 1, IsIncluded: true, IsDescending: false)),
            Metadata: metadata);

        var builder = new IndexScriptBuilder(_formatter);
        var statement = builder.BuildCreateIndexStatement(table, index, "Order", "IX_Order_Status", SmoFormatOptions.Default);

        Assert.Equal("IX_Order_Status", statement.Name.Value);
        Assert.Equal(SortOrder.Descending, statement.Columns[0].SortOrder);
        Assert.Single(statement.IncludeColumns);
        Assert.IsType<BooleanParenthesisExpression>(statement.FilterPredicate);
        Assert.Equal("FG_DATA", statement.OnFileGroupOrPartitionScheme!.Name.Identifier.Value);

        var compressionOptions = statement.IndexOptions.OfType<DataCompressionOption>().ToList();
        Assert.Equal(2, compressionOptions.Count);

        var pageCompression = Assert.Single(compressionOptions, option => option.CompressionLevel == DataCompressionLevel.Page);
        Assert.Single(pageCompression.PartitionRanges);
        var firstRange = pageCompression.PartitionRanges[0];
        Assert.Equal("1", ((IntegerLiteral)firstRange.From!).Value);
        Assert.Equal("2", ((IntegerLiteral)firstRange.To!).Value);

        var rowCompression = Assert.Single(compressionOptions, option => option.CompressionLevel == DataCompressionLevel.Row);
        Assert.Single(rowCompression.PartitionRanges);
    }

    [Fact]
    public void BuildDisableIndexStatement_targets_table_and_index()
    {
        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray<SmoColumnDefinition>.Empty,
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new IndexScriptBuilder(_formatter);
        var statement = builder.BuildDisableIndexStatement(table, "Order", "IX_Order_Status", SmoFormatOptions.Default);

        Assert.Equal("IX_Order_Status", statement.Name.Value);
        Assert.Equal(AlterIndexType.Disable, statement.AlterIndexType);
        Assert.Equal("Order", statement.OnName.Identifiers[1].Value);
    }
}
