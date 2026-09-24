using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Smo.Tests;

public class PerTableWriterTests
{
    [Fact]
    public void Generate_respects_identifier_quote_strategy()
    {
        var column = new SmoColumnDefinition(
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
    public void Generate_includes_ms_description_extended_properties()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: options);

        var customerTable = Assert.Single(
            smoModel.Tables,
            table => table.LogicalName.Equals("Customer", StringComparison.Ordinal));

        var writer = new PerTableWriter();
        var result = writer.Generate(customerTable, options);

        Assert.Contains(
            "@name=N'MS_Description', @value=N'Stores customer records for AppCore'",
            result.Script,
            StringComparison.Ordinal);

        Assert.Contains(
            "@level2type=N'COLUMN',@level2name=N'Email'",
            result.Script,
            StringComparison.Ordinal);

        Assert.Contains(
            "@value=N'Customer first name'",
            result.Script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_includes_configured_header_when_enabled()
    {
        var column = new SmoColumnDefinition(
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

    [Fact]
    public void Generate_inlines_foreign_keys_with_referencing_columns()
    {
        var idColumn = new SmoColumnDefinition(
            PhysicalName: "ID",
            Name: "Id",
            LogicalName: "Id",
            DataType: DataType.BigInt,
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

        var foreignKeyColumn = new SmoColumnDefinition(
            PhysicalName: "CITYID",
            Name: "CityId",
            LogicalName: "CityId",
            DataType: DataType.BigInt,
            Nullable: false,
            IsIdentity: false,
            IdentitySeed: 0,
            IdentityIncrement: 0,
            IsComputed: false,
            ComputedExpression: null,
            DefaultExpression: null,
            Collation: null,
            Description: null,
            DefaultConstraint: null,
            CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty);

        var nameColumn = new SmoColumnDefinition(
            PhysicalName: "NAME",
            Name: "Name",
            LogicalName: "Name",
            DataType: DataType.NVarChar(100),
            Nullable: true,
            IsIdentity: false,
            IdentitySeed: 0,
            IdentityIncrement: 0,
            IsComputed: false,
            ComputedExpression: null,
            DefaultExpression: null,
            Collation: null,
            Description: null,
            DefaultConstraint: null,
            CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty);

        var foreignKey = new SmoForeignKeyDefinition(
            Name: "FK_Order_City_CityId",
            Columns: ImmutableArray.Create(foreignKeyColumn.Name),
            ReferencedModule: "AppCore",
            ReferencedTable: "OSUSR_DEF_CITY",
            ReferencedSchema: "dbo",
            ReferencedColumns: ImmutableArray.Create("Id"),
            ReferencedLogicalTable: "City",
            DeleteAction: ForeignKeyAction.NoAction,
            IsNoCheck: false);

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray.Create(idColumn, foreignKeyColumn, nameColumn),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray.Create(foreignKey),
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var writer = new PerTableWriter();
        var result = writer.Generate(table, SmoBuildOptions.Default);

        var script = result.Script;
        var cityIndex = script.IndexOf("[CityId]", StringComparison.Ordinal);
        Assert.True(cityIndex >= 0);

        var constraintIndex = script.IndexOf("CONSTRAINT [FK_Order_City_CityId]", cityIndex, StringComparison.Ordinal);
        Assert.True(constraintIndex > cityIndex);

        var foreignKeyIndex = script.IndexOf("FOREIGN KEY ([CityId])", constraintIndex, StringComparison.Ordinal);
        Assert.True(foreignKeyIndex > constraintIndex);

        var nextColumnIndex = script.IndexOf("[Name]", foreignKeyIndex, StringComparison.Ordinal);
        Assert.True(nextColumnIndex > foreignKeyIndex);
    }

    [Fact]
    public void Generate_preserves_column_order_from_model()
    {
        var moduleName = ModuleName.Create("Inventory").Value;
        var tableName = TableName.Create("OSUSR_INV_ITEM").Value;
        var schema = SchemaName.Create("inv").Value;

        var gammaAttribute = AttributeModel.Create(
            AttributeName.Create("Gamma").Value,
            ColumnName.Create("GAMMA").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;

        var alphaAttribute = AttributeModel.Create(
            AttributeName.Create("Alpha").Value,
            ColumnName.Create("ALPHA").Value,
            dataType: "Integer",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;

        var betaAttribute = AttributeModel.Create(
            AttributeName.Create("Beta").Value,
            ColumnName.Create("BETA").Value,
            dataType: "Long Integer",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;

        var entity = EntityModel.Create(
            moduleName,
            EntityName.Create("Item").Value,
            tableName,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { gammaAttribute, alphaAttribute, betaAttribute },
            metadata: EntityMetadata.Empty).Value;

        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { entity }).Value;
        var osmModel = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var factory = new SmoModelFactory();
        var smoModel = factory.Create(osmModel, decisions);
        var table = Assert.Single(smoModel.Tables);

        Assert.Equal(new[] { "Gamma", "Alpha", "Beta" }, table.Columns.Select(column => column.Name));

        var writer = new PerTableWriter();
        var result = writer.Generate(table, SmoBuildOptions.Default);

        var gammaIndex = result.Script.IndexOf("[Gamma]", StringComparison.Ordinal);
        var alphaIndex = result.Script.IndexOf("[Alpha]", StringComparison.Ordinal);
        var betaIndex = result.Script.IndexOf("[Beta]", StringComparison.Ordinal);

        Assert.True(gammaIndex >= 0, "Gamma column not found in script.");
        Assert.True(alphaIndex >= 0, "Alpha column not found in script.");
        Assert.True(betaIndex >= 0, "Beta column not found in script.");
        Assert.True(gammaIndex < alphaIndex, "Gamma column should appear before Alpha column.");
        Assert.True(alphaIndex < betaIndex, "Alpha column should appear before Beta column.");
    }
}
