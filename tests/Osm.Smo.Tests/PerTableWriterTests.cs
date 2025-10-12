using System;
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

    [Fact]
    public void Generate_includes_configured_header_when_enabled()
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
        var headerOptions = PerTableHeaderOptions.EnabledTemplate with
        {
            Source = "model.json",
            Profile = "profile.json",
            Decisions = "Mode=EvidenceGated",
            FingerprintAlgorithm = "SHA256",
            FingerprintHash = "abc123",
        };

        var options = SmoBuildOptions.Default.WithHeaderOptions(headerOptions);
        var headerItems = ImmutableArray.Create(
            PerTableHeaderItem.Create("LogicalName", table.LogicalName),
            PerTableHeaderItem.Create("Module", table.Module));

        var result = writer.Generate(table, options, headerItems);

        Assert.StartsWith("/*", result.Script, StringComparison.Ordinal);
        Assert.Contains("Source: model.json", result.Script, StringComparison.Ordinal);
        Assert.Contains("Profile: profile.json", result.Script, StringComparison.Ordinal);
        Assert.Contains("Decisions: Mode=EvidenceGated", result.Script, StringComparison.Ordinal);
        Assert.Contains("SHA256 Fingerprint: abc123", result.Script, StringComparison.Ordinal);
        Assert.Contains("LogicalName: Customer", result.Script, StringComparison.Ordinal);
        Assert.Contains("Module: Sales", result.Script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [dbo].[Customer]", result.Script, StringComparison.Ordinal);
    }
}
