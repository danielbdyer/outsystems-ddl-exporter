using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Domain.Configuration;
using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class CreateTableStatementBuilderTests
{
    private readonly SqlScriptFormatter _formatter = new();

    [Fact]
    public void BuildCreateTableStatement_inlines_single_column_primary_key()
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

        var index = new SmoIndexDefinition(
            Name: "PK_Order_Id",
            IsUnique: true,
            IsPrimaryKey: true,
            IsPlatformAuto: false,
            Columns: ImmutableArray.Create(new SmoIndexColumnDefinition("Id", 0, false, false)),
            Metadata: SmoIndexMetadata.Empty);

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray.Create(column),
            Indexes: ImmutableArray.Create(index),
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new CreateTableStatementBuilder(_formatter);
        var statement = builder.BuildCreateTableStatement(table, "Order", SmoBuildOptions.Default);

        Assert.NotNull(statement.Definition);
        var singleColumn = Assert.Single(statement.Definition.ColumnDefinitions);
        Assert.Equal("Id", singleColumn.ColumnIdentifier.Value);

        var inlineConstraint = Assert.Single(singleColumn.Constraints.OfType<UniqueConstraintDefinition>());
        Assert.True(inlineConstraint.IsPrimaryKey);
        Assert.True(inlineConstraint.Clustered);
        Assert.Equal("PK_Order_Id", inlineConstraint.ConstraintIdentifier.Value);
        Assert.Empty(statement.Definition.TableConstraints);
    }

    [Fact]
    public void AddForeignKeys_attaches_constraints_and_tracks_trust()
    {
        var column = new SmoColumnDefinition(
            PhysicalName: "CITYID",
            Name: "CityId",
            LogicalName: "CityId",
            DataType: DataType.Int,
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

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray.Create(column),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray.Create(new SmoForeignKeyDefinition(
                Name: "FK_OSUSR_SALES_ORDER_CITY",
                Columns: ImmutableArray.Create("CityId"),
                ReferencedModule: "Core",
                ReferencedTable: "OSUSR_CORE_CITY",
                ReferencedSchema: "dbo",
                ReferencedColumns: ImmutableArray.Create("Id"),
                ReferencedLogicalTable: "City",
                DeleteAction: ForeignKeyAction.SetNull,
                IsNoCheck: true)),
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new CreateTableStatementBuilder(_formatter);
        var statement = builder.BuildCreateTableStatement(table, "Order", SmoBuildOptions.Default);
        var foreignKeyNames = builder.AddForeignKeys(statement, table, "Order", SmoBuildOptions.Default, out var trustLookup);

        var resolvedName = Assert.Single(foreignKeyNames);
        Assert.Equal("FK_Order_CITY", resolvedName);
        Assert.True(trustLookup[resolvedName]);

        var columnDefinition = Assert.Single(statement.Definition!.ColumnDefinitions);
        var fkConstraint = Assert.Single(columnDefinition.Constraints.OfType<ForeignKeyConstraintDefinition>());
        Assert.Equal("CityId", fkConstraint.Columns[0].Value);
        Assert.Equal(DeleteUpdateAction.SetNull, fkConstraint.DeleteAction);
    }

    [Fact]
    public void AddForeignKeys_emits_all_columns_for_composite_relationship()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadCompositeForeignKeyArtifacts();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, snapshot, options);
        var childTable = Assert.Single(smoModel.Tables.Where(t => t.LogicalName.Equals("Child", StringComparison.Ordinal)));

        var builder = new CreateTableStatementBuilder(_formatter);
        var statement = builder.BuildCreateTableStatement(childTable, childTable.Name, options);
        var foreignKeyNames = builder.AddForeignKeys(statement, childTable, childTable.Name, options, out _);

        var resolvedName = Assert.Single(foreignKeyNames);
        Assert.Equal("FK_Child_Parent", resolvedName);

        var constraint = Assert.Single(statement.Definition!.TableConstraints.OfType<ForeignKeyConstraintDefinition>());
        Assert.Collection(
            constraint.Columns,
            column => Assert.Equal("ParentId", column.Value),
            column => Assert.Equal("TenantId", column.Value));
        Assert.Collection(
            constraint.ReferencedTableColumns,
            column => Assert.Equal("Id", column.Value),
            column => Assert.Equal("TenantId", column.Value));
    }

    [Fact]
    public void BuildCreateTableStatement_WritesDecimalPrecisionBeforeScale()
    {
        var column = new SmoColumnDefinition(
            PhysicalName: "CREDITLIMIT",
            Name: "CreditLimit",
            LogicalName: "CreditLimit",
            DataType: DataType.Decimal(37, 8),
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

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: ImmutableArray.Create(column),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var builder = new CreateTableStatementBuilder(_formatter);
        var statement = builder.BuildCreateTableStatement(table, "Order", SmoBuildOptions.Default);

        var columnDefinition = Assert.Single(statement.Definition!.ColumnDefinitions);
        var dataType = Assert.IsType<SqlDataTypeReference>(columnDefinition.DataType);
        Assert.Equal(SqlDataTypeOption.Decimal, dataType.SqlDataTypeOption);
        Assert.Collection(
            dataType.Parameters,
            first =>
            {
                var precision = Assert.IsType<IntegerLiteral>(first);
                Assert.Equal("37", precision.Value);
            },
            second =>
            {
                var scale = Assert.IsType<IntegerLiteral>(second);
                Assert.Equal("8", scale.Value);
            });
    }
}
