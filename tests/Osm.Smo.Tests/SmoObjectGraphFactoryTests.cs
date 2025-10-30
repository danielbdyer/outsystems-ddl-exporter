using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;
using SmoIndex = Microsoft.SqlServer.Management.Smo.Index;
using SystemIndex = System.Index;
using Osm.Json;

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

        var uniqueIndex = customerTable.Indexes["UIX_Customer_Email"];
        Assert.True(uniqueIndex.IsUnique);
        Assert.Equal(IndexKeyType.DriUniqueKey, uniqueIndex.IndexKeyType);
        Assert.Contains(uniqueIndex.IndexedColumns.Cast<IndexedColumn>(), column =>
            column.Name.Equals("Email", StringComparison.OrdinalIgnoreCase) && !column.IsIncluded);
        Assert.Equal("[EMAIL] IS NOT NULL", uniqueIndex.FilterDefinition);
        Assert.True(uniqueIndex.IgnoreDuplicateKeys);
        Assert.False(uniqueIndex.PadIndex);
        Assert.False(uniqueIndex.DisallowRowLocks);
        Assert.False(uniqueIndex.DisallowPageLocks);
        Assert.False(uniqueIndex.NoAutomaticRecomputation);
        Assert.Equal(85, uniqueIndex.FillFactor);
        Assert.Equal("FG_Customers", uniqueIndex.FileGroup);

        var foreignKey = Assert.Single(customerTable.ForeignKeys.Cast<ForeignKey>());
        Assert.Equal("FK_Customer_City_CityId", foreignKey.Name);
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

        var uniqueIndex = Assert.IsType<SmoIndex>(customerTable.Indexes["UIX_Customer_Email"]);
        Assert.Equal("UIX_Customer_Email", uniqueIndex.Name);
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

        Assert.Equal("FK_Child_Parent_ParentId_TenantId", foreignKey.Name);
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

    [Fact]
    public void CreateTable_marks_untrusted_foreign_keys_as_not_checked()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var profile = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var decisions = _policy.Decide(model, profile, TighteningOptions.Default);
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);

        var triggeredCoordinate = new ColumnCoordinate(
            new SchemaName("dbo"),
            new TableName("OSUSR_XYZ_JOBRUN"),
            new ColumnName("TRIGGEREDBYUSERID"));

        Assert.True(decisions.ForeignKeys.TryGetValue(triggeredCoordinate, out var triggeredDecision));
        var enabledDecision = triggeredDecision with { CreateConstraint = true };
        var updatedForeignKeys = decisions.ForeignKeys.SetItem(triggeredCoordinate, enabledDecision);
        var updatedDecisions = decisions with { ForeignKeys = updatedForeignKeys };

        var supplementalEntities = LoadSupplementalUserEntities();
        var smoModel = _modelFactory.Create(model, updatedDecisions, profile, options, supplementalEntities);

        var tables = _factory.CreateTables(smoModel, options);
        var jobRunTable = Assert.Single(tables.Where(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase)));
        var jobRunForeignKey = Assert.Single(jobRunTable.ForeignKeys.Cast<ForeignKey>());

        Assert.False(jobRunForeignKey.IsChecked);
        Assert.Equal("ossys_User", jobRunForeignKey.ReferencedTable);
        Assert.Equal("dbo", jobRunForeignKey.ReferencedTableSchema);

        var customerTable = Assert.Single(tables.Where(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase)));
        var cityForeignKey = Assert.Single(customerTable.ForeignKeys.Cast<ForeignKey>());
        Assert.True(cityForeignKey.IsChecked);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static ImmutableArray<EntityModel> LoadSupplementalUserEntities()
    {
        var supplementalPath = Path.Combine(FixtureFile.RepositoryRoot, "config", "supplemental", "ossys-user.json");
        using var stream = File.OpenRead(supplementalPath);
        var deserializer = new ModelJsonDeserializer();
        var supplementalResult = deserializer.Deserialize(stream);

        if (!supplementalResult.IsSuccess)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, supplementalResult.Errors.Select(error => error.Message)));
        }

        return supplementalResult.Value.Modules
            .SelectMany(module => module.Entities)
            .ToImmutableArray();
    }
}
