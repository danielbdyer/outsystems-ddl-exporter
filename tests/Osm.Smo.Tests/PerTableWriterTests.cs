using System.Collections.Immutable;
using Microsoft.SqlServer.Management.Smo;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class PerTableWriterTests
{
    [Fact]
    public void Generate_respects_identifier_quote_strategy()
    {
        var column = new SmoColumnDefinition(
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
            CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty);

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_CUSTOMER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Customer",
            Description: null,
            Columns: ImmutableArray.Create(column),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var writer = new PerTableWriter();
        var format = SmoFormatOptions.Default.WithIdentifierQuoteStrategy(IdentifierQuoteStrategy.DoubleQuote);
        var options = SmoBuildOptions.Default.WithFormat(format);

        var result = writer.Generate(table, options);

        Assert.Contains("CREATE TABLE \"dbo\".\"Customer\"", result.Script);
        Assert.Contains("\"Id\"", result.Script);
        Assert.Contains("INT", result.Script);
    }
}
