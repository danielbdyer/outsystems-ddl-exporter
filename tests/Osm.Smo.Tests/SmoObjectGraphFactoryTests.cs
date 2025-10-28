using System;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;
using SmoIndex = Microsoft.SqlServer.Management.Smo.Index;
using SystemIndex = System.Index;

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
        Assert.Equal("FK_Customer_Osusr_Def_City_CityId", foreignKey.Name);
        Assert.True(foreignKey.IsChecked);
        Assert.Equal(ForeignKeyAction.NoAction, foreignKey.DeleteAction);
        Assert.Equal("OSUSR_DEF_CITY", foreignKey.ReferencedTable);
        Assert.Equal("dbo", foreignKey.ReferencedTableSchema);
        Assert.Contains(foreignKey.Columns.Cast<ForeignKeyColumn>(), column =>
            column.Name.Equals("CityId", StringComparison.OrdinalIgnoreCase) &&
            column.ReferencedColumn.Equals("Id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateTable_uses_smo_index_type_when_system_index_is_in_scope()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var profile = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var decisions = _policy.Decide(model, profile, TighteningOptions.Default);
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var smoModel = _modelFactory.Create(model, decisions, profile, options);

        var tables = _factory.CreateTables(smoModel, options);
        var customerTable = Assert.Single(tables.Where(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase)));

        var uniqueIndex = Assert.IsType<SmoIndex>(customerTable.Indexes["IDX_Customer_Email"]);
        Assert.Equal("IDX_Customer_Email", uniqueIndex.Name);
        Assert.Equal(IndexKeyType.DriUniqueKey, uniqueIndex.IndexKeyType);

        var systemIndex = SystemIndex.Start;
        Assert.False(systemIndex.IsFromEnd);
        Assert.Equal(0, systemIndex.GetOffset(5));
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

    [Fact]
    public void CreateTable_emits_all_columns_for_composite_foreign_keys()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadCompositeForeignKeyArtifacts();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var smoModel = _modelFactory.Create(model, decisions, snapshot, options);

        var tables = _factory.CreateTables(smoModel, options);
        var childTable = Assert.Single(tables.Where(t => t.Name.Equals("OSUSR_M_CHILD", StringComparison.OrdinalIgnoreCase)));
        var foreignKey = Assert.Single(childTable.ForeignKeys.Cast<ForeignKey>());

        Assert.Equal("FK_OSUSR_M_CHILD_PARENT", foreignKey.Name);
        Assert.Collection(
            foreignKey.Columns.Cast<ForeignKeyColumn>(),
            column =>
            {
                Assert.Equal("ParentId", column.Name, ignoreCase: true, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false);
                Assert.Equal("Id", column.ReferencedColumn, ignoreCase: true, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false);
            },
            column =>
            {
                Assert.Equal("TenantId", column.Name, ignoreCase: true, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false);
                Assert.Equal("TenantId", column.ReferencedColumn, ignoreCase: true, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false);
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}
