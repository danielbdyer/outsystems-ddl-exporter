using System;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Smo.Tests;

public sealed class SmoObjectGraphFactoryTests : IDisposable
{
    private readonly SmoObjectGraphFactory _factory;
    private readonly SmoModelFactory _modelFactory;
    private readonly TighteningPolicy _policy;

    public SmoObjectGraphFactoryTests()
    {
        _factory = new SmoObjectGraphFactory();
        _modelFactory = new SmoModelFactory();
        _policy = new TighteningPolicy();
    }

    [Fact]
    public void CreateTable_populates_columns_indexes_and_foreign_keys()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var profile = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var decisions = _policy.Decide(model, profile, TighteningOptions.Default);
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var smoModel = _modelFactory.Create(model, decisions, profile, options);

        var tables = _factory.CreateTables(smoModel, options);
        var customerTable = Assert.Single(tables.Where(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal("dbo", customerTable.Schema);
        Assert.Equal(options.DefaultCatalogName, customerTable.Parent?.Name);

        var idColumn = customerTable.Columns["Id"];
        Assert.False(idColumn.Nullable);
        Assert.True(idColumn.Identity);
        Assert.Equal(1, idColumn.IdentitySeed);
        Assert.Equal(1, idColumn.IdentityIncrement);

        var emailColumn = customerTable.Columns["Email"];
        Assert.False(emailColumn.Nullable);
        Assert.Equal(SqlDataType.NVarChar, emailColumn.DataType.SqlDataType);

        var uniqueIndex = customerTable.Indexes["IDX_Customer_Email"];
        Assert.True(uniqueIndex.IsUnique);
        Assert.Equal(IndexKeyType.DriUniqueKey, uniqueIndex.IndexKeyType);
        Assert.Contains(uniqueIndex.IndexedColumns.Cast<IndexedColumn>(), column =>
            column.Name.Equals("Email", StringComparison.OrdinalIgnoreCase) && !column.IsIncluded);

        var foreignKey = Assert.Single(customerTable.ForeignKeys.Cast<ForeignKey>());
        Assert.Equal("FK_Customer_CityId", foreignKey.Name);
        Assert.True(foreignKey.IsChecked);
        Assert.Equal(ForeignKeyAction.NoAction, foreignKey.DeleteAction);
        Assert.Equal("OSUSR_DEF_CITY", foreignKey.ReferencedTable);
        Assert.Equal("dbo", foreignKey.ReferencedTableSchema);
        Assert.Contains(foreignKey.Columns.Cast<ForeignKeyColumn>(), column =>
            column.Name.Equals("CityId", StringComparison.OrdinalIgnoreCase) &&
            column.ReferencedColumn.Equals("Id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateTable_respects_naming_overrides_for_referenced_tables()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var profile = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var decisions = _policy.Decide(model, profile, TighteningOptions.Default);
        var baseOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);

        var overrideRuleResult = NamingOverrideRule.Create(
            schema: "dbo",
            table: "OSUSR_DEF_CITY",
            module: null,
            logicalName: null,
            target: "CityArchive");

        Assert.True(overrideRuleResult.IsSuccess);
        var overrides = NamingOverrideOptions.Create(new[] { overrideRuleResult.Value });
        Assert.True(overrides.IsSuccess);

        var options = baseOptions.WithNamingOverrides(overrides.Value);
        var smoModel = _modelFactory.Create(model, decisions, profile, options);

        var tables = _factory.CreateTables(smoModel, options);
        var customerTable = tables.Single(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        var foreignKey = Assert.Single(customerTable.ForeignKeys.Cast<ForeignKey>());

        Assert.Equal("CityArchive", foreignKey.ReferencedTable);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}
