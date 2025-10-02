using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Smo.Tests;

public class SmoModelFactoryTests
{
    private static (Osm.Domain.Model.OsmModel model, PolicyDecisionSet decisions) LoadEdgeCaseDecisions()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        return (model, decisions);
    }

    [Fact]
    public void Build_creates_tables_with_policy_driven_nullability_and_foreign_keys()
    {
        var (model, decisions) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission));

        var customerTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        var emailColumn = customerTable.Columns.Single(c => c.Name.Equals("Email", StringComparison.Ordinal));
        var cityColumn = customerTable.Columns.Single(c => c.Name.Equals("CityId", StringComparison.Ordinal));
        Assert.False(emailColumn.Nullable);
        Assert.False(cityColumn.Nullable);
        Assert.Equal(SqlDataType.NVarChar, emailColumn.DataType.SqlDataType);
        Assert.Equal(255, emailColumn.DataType.MaximumLength);
        Assert.DoesNotContain(customerTable.Columns, c => c.Name.Equals("LegacyCode", StringComparison.Ordinal));

        var pk = customerTable.Indexes.Single(i => i.IsPrimaryKey);
        Assert.Equal("PK_Customer", pk.Name);
        Assert.Collection(pk.Columns.OrderBy(c => c.Ordinal),
            col => Assert.Equal("Id", col.Name));

        var emailIndex = customerTable.Indexes.Single(i => i.Name.Equals("IDX_Customer_Email", StringComparison.OrdinalIgnoreCase));
        Assert.True(emailIndex.IsUnique);

        var cityForeignKey = customerTable.ForeignKeys.Single();
        Assert.Equal("FK_Customer_CityId", cityForeignKey.Name);
        Assert.Equal("OSUSR_DEF_CITY", cityForeignKey.ReferencedTable);
        Assert.Equal("dbo", cityForeignKey.ReferencedSchema);
        Assert.Equal(ForeignKeyAction.NoAction, cityForeignKey.DeleteAction);
        Assert.Equal("City", cityForeignKey.ReferencedLogicalTable);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));
        var jobRunTriggeredByColumn = jobRunTable.Columns.Single(c => c.Name.Equals("TriggeredByUserId", StringComparison.Ordinal));
        Assert.True(jobRunTriggeredByColumn.Nullable);
        Assert.Empty(jobRunTable.ForeignKeys);

        var billingTable = smoModel.Tables.Single(t => t.Name.Equals("BILLING_ACCOUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("billing", billingTable.Schema);
        var accountNumberColumn = billingTable.Columns.Single(c => c.Name.Equals("AccountNumber", StringComparison.Ordinal));
        Assert.Equal(SqlDataType.VarChar, accountNumberColumn.DataType.SqlDataType);
        Assert.Equal(50, accountNumberColumn.DataType.MaximumLength);
    }

    [Fact]
    public void Build_excludes_platform_auto_indexes_when_disabled()
    {
        var (model, decisions) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);

        var smoModel = factory.Create(model, decisions, smoOptions);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));

        Assert.All(jobRunTable.Indexes, index => Assert.False(index.IsPlatformAuto));
        Assert.DoesNotContain(jobRunTable.Indexes, index =>
            index.Name.Contains("OSIDX", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_respects_platform_auto_index_toggle()
    {
        var (model, decisions) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var options = new SmoBuildOptions(
            "OutSystems",
            IncludePlatformAutoIndexes: true,
            EmitConcatenatedConstraints: false,
            SanitizeModuleNames: true,
            NamingOverrides: NamingOverrideOptions.Empty);
        var smoModel = factory.Create(model, decisions, options);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));
        var hasPlatformIndex = jobRunTable.Indexes.Any(i => i.Name.Equals("OSIDX_JobRun_CreatedOn", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasPlatformIndex);
    }

    [Fact]
    public void Build_applies_unique_decisions_from_policy()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var options = TighteningOptions.Default;
        var decisions = new TighteningPolicy().Decide(model, snapshot, options);
        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);

        var smoModel = factory.Create(model, decisions, smoOptions);

        var userTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_U_USER", StringComparison.OrdinalIgnoreCase));
        var uniqueIndex = userTable.Indexes.Single(i => i.Name.Equals("UX_User_Email", StringComparison.OrdinalIgnoreCase));
        Assert.False(uniqueIndex.IsUnique);
    }

    [Fact]
    public void Build_enforces_unique_even_when_remediation_required()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUniqueWithDuplicates);
        var defaults = TighteningOptions.Default;
        var aggressivePolicy = PolicyOptions.Create(TighteningMode.Aggressive, defaults.Policy.NullBudget).Value;
        var aggressiveOptions = TighteningOptions.Create(
            aggressivePolicy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            defaults.Emission,
            defaults.Mocking).Value;

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, aggressiveOptions);

        var entity = model.Modules.Single().Entities.Single();
        var indexModel = entity.Indexes.Single();
        var coordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, indexModel.Name);
        var indexDecision = decisions.UniqueIndexes[coordinate];
        Assert.True(indexDecision.EnforceUnique);
        Assert.True(indexDecision.RequiresRemediation);

        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(aggressiveOptions.Emission);
        var smoModel = factory.Create(model, decisions, smoOptions);

        var userTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_U_USER", StringComparison.OrdinalIgnoreCase));
        var uniqueIndex = userTable.Indexes.Single(i => i.Name.Equals("UX_User_Email", StringComparison.OrdinalIgnoreCase));
        Assert.True(uniqueIndex.IsUnique);
    }

    [Fact]
    public void Build_includes_active_check_constraints()
    {
        var moduleName = ModuleName.Create("Finance").Value;
        var entityName = EntityName.Create("Invoice").Value;
        var schema = SchemaName.Create("dbo").Value;
        var tableName = TableName.Create("OSUSR_FIN_INVOICE").Value;

        var idAttribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None).Value;

        var checkConstraint = CheckConstraintModel.Create(
            ConstraintName.Create("CK_Invoice_Positive").Value,
            "Id > 0",
            isActive: true).Value;

        var entity = EntityModel.Create(
            moduleName,
            entityName,
            tableName,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { idAttribute },
            checkConstraints: new[] { checkConstraint }).Value;

        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { entity }).Value;
        var osmModel = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty);

        var factory = new SmoModelFactory();
        var smoModel = factory.Create(osmModel, decisions, SmoBuildOptions.Default);

        var table = smoModel.Tables.Single();
        var constraint = table.CheckConstraints.Single();
        Assert.Equal("CK_Invoice_Positive", constraint.Name);
        Assert.Equal("Id > 0", constraint.Definition);
    }
}
