using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Smo.Tests;

public class SmoModelFactoryTests
{
    private static (Osm.Domain.Model.OsmModel model, PolicyDecisionSet decisions, ProfileSnapshot snapshot) LoadEdgeCaseDecisions()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        return (model, decisions, snapshot);
    }

    [Fact]
    public void Build_creates_tables_with_policy_driven_nullability_and_foreign_keys()
    {
        var (model, decisions, snapshot) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(
            model,
            decisions,
            profile: snapshot,
            options: SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission));

        var customerTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        var idColumn = customerTable.Columns.Single(c => c.Name.Equals("Id", StringComparison.Ordinal));
        var emailColumn = customerTable.Columns.Single(c => c.Name.Equals("Email", StringComparison.Ordinal));
        var cityColumn = customerTable.Columns.Single(c => c.Name.Equals("CityId", StringComparison.Ordinal));
        Assert.Equal(SqlDataType.BigInt, idColumn.DataType.SqlDataType);
        Assert.True(idColumn.IsIdentity);
        Assert.False(emailColumn.Nullable);
        Assert.False(cityColumn.Nullable);
        Assert.Equal(SqlDataType.BigInt, cityColumn.DataType.SqlDataType);
        Assert.Equal(SqlDataType.NVarChar, emailColumn.DataType.SqlDataType);
        Assert.Equal(255, emailColumn.DataType.MaximumLength);
        Assert.DoesNotContain(customerTable.Columns, c => c.Name.Equals("LegacyCode", StringComparison.Ordinal));

        var pk = customerTable.Indexes.Single(i => i.IsPrimaryKey);
        Assert.Equal("PK_Customer", pk.Name);
        Assert.Collection(pk.Columns.OrderBy(c => c.Ordinal),
            col => Assert.Equal("Id", col.Name));

        var emailIndex = customerTable.Indexes.Single(i => i.Name.Equals("IDX_Customer_Email", StringComparison.OrdinalIgnoreCase));
        Assert.True(emailIndex.IsUnique);
        Assert.Equal(85, emailIndex.Metadata.FillFactor);
        Assert.True(emailIndex.Metadata.IgnoreDuplicateKey);
        Assert.Equal("[EMAIL] IS NOT NULL", emailIndex.Metadata.FilterDefinition);
        var emailDataSpace = emailIndex.Metadata.DataSpace;
        Assert.NotNull(emailDataSpace);
        Assert.Equal("FG_Customers", emailDataSpace!.Name);
        Assert.Equal("ROWS_FILEGROUP", emailDataSpace.Type);

        var nameIndex = customerTable.Indexes.Single(i => i.Name.Equals("IDX_Customer_Name", StringComparison.OrdinalIgnoreCase));
        Assert.True(nameIndex.Metadata.IsDisabled);
        Assert.True(nameIndex.Metadata.StatisticsNoRecompute);

        var cityForeignKey = customerTable.ForeignKeys.Single();
        Assert.Equal("FK_Customer_CityId", cityForeignKey.Name);
        Assert.Equal("OSUSR_DEF_CITY", cityForeignKey.ReferencedTable);
        Assert.Equal("dbo", cityForeignKey.ReferencedSchema);
        Assert.Equal(ForeignKeyAction.NoAction, cityForeignKey.DeleteAction);
        Assert.Equal("City", cityForeignKey.ReferencedLogicalTable);
        Assert.False(cityForeignKey.IsNoCheck);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));
        var jobRunTriggeredByColumn = jobRunTable.Columns.Single(c => c.Name.Equals("TriggeredByUserId", StringComparison.Ordinal));
        Assert.True(jobRunTriggeredByColumn.Nullable);
        Assert.Empty(jobRunTable.ForeignKeys);
        var triggerDefinition = Assert.Single(jobRunTable.Triggers);
        Assert.Equal("TR_OSUSR_XYZ_JOBRUN_AUDIT", triggerDefinition.Name);
        Assert.True(triggerDefinition.IsDisabled);
        Assert.Contains("CREATE TRIGGER", triggerDefinition.Definition);

        var billingTable = smoModel.Tables.Single(t => t.Name.Equals("BILLING_ACCOUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("billing", billingTable.Schema);
        var accountNumberColumn = billingTable.Columns.Single(c => c.Name.Equals("AccountNumber", StringComparison.Ordinal));
        Assert.Equal(SqlDataType.VarChar, accountNumberColumn.DataType.SqlDataType);
        Assert.Equal(50, accountNumberColumn.DataType.MaximumLength);
    }

    [Fact]
    public void Build_maps_on_disk_default_and_check_constraints()
    {
        var (model, decisions, snapshot) = LoadEdgeCaseDecisions();
        var module = model.Modules.First();
        var entity = module.Entities.First();
        var attribute = entity.Attributes.First(a => !a.IsIdentifier);

        var updatedOnDisk = attribute.OnDisk with
        {
            DefaultDefinition = "((1))",
            DefaultConstraint = new AttributeOnDiskDefaultConstraint("DF_Custom_Default", "((1))", IsNotTrusted: false),
            CheckConstraints = ImmutableArray.Create(
                new AttributeOnDiskCheckConstraint("CK_Custom_Check", "([" + attribute.ColumnName.Value + "] > (0))", IsNotTrusted: true))
        };

        var updatedAttribute = attribute with { OnDisk = updatedOnDisk };
        var updatedAttributes = entity.Attributes.Replace(attribute, updatedAttribute);
        var updatedEntity = entity with { Attributes = updatedAttributes };
        var updatedModule = module with { Entities = module.Entities.Replace(entity, updatedEntity) };
        var updatedModel = model with { Modules = model.Modules.Replace(module, updatedModule) };

        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var smoModel = factory.Create(updatedModel, decisions, profile: snapshot, options: smoOptions);

        var smoTable = smoModel.Tables.Single(t => t.Name.Equals(entity.PhysicalName.Value, StringComparison.OrdinalIgnoreCase));
        var smoColumn = smoTable.Columns.Single(c => c.Name.Equals(updatedAttribute.LogicalName.Value, StringComparison.Ordinal));

        Assert.Equal("((1))", smoColumn.DefaultExpression);
        Assert.NotNull(smoColumn.DefaultConstraint);
        Assert.Equal("DF_Custom_Default", smoColumn.DefaultConstraint!.Name);
        Assert.Equal("((1))", smoColumn.DefaultConstraint!.Expression);
        Assert.True(smoColumn.CheckConstraints.Any());
        var checkConstraint = Assert.Single(smoColumn.CheckConstraints);
        Assert.Equal("CK_Custom_Check", checkConstraint.Name);
        Assert.Equal("([" + updatedAttribute.ColumnName.Value + "] > (0))", checkConstraint.Expression);
        Assert.True(checkConstraint.IsNotTrusted);
    }

    [Fact]
    public void Build_excludes_platform_auto_indexes_when_disabled()
    {
        var (model, decisions, snapshot) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);

        var smoModel = factory.Create(model, decisions, profile: snapshot, options: smoOptions);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));

        Assert.All(jobRunTable.Indexes, index => Assert.False(index.IsPlatformAuto));
        Assert.DoesNotContain(jobRunTable.Indexes, index =>
            index.Name.Contains("OSIDX", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_respects_platform_auto_index_toggle()
    {
        var (model, decisions, snapshot) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var options = new SmoBuildOptions(
            "OutSystems",
            IncludePlatformAutoIndexes: true,
            EmitBareTableOnly: false,
            SanitizeModuleNames: true,
            NamingOverrides: NamingOverrideOptions.Empty);
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: options);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));
        var hasPlatformIndex = jobRunTable.Indexes.Any(i => i.Name.Equals("OSIDX_JobRun_CreatedOn", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasPlatformIndex);

        var platformIndex = jobRunTable.Indexes.Single(i => i.Name.Equals("OSIDX_JobRun_CreatedOn", StringComparison.OrdinalIgnoreCase));
        Assert.False(platformIndex.Metadata.AllowRowLocks);
        var jobRunDataSpace = platformIndex.Metadata.DataSpace;
        Assert.NotNull(jobRunDataSpace);
        Assert.Equal("PS_JobRun", jobRunDataSpace!.Name);
        Assert.Equal("PARTITION_SCHEME", jobRunDataSpace.Type);
        var partition = Assert.Single(platformIndex.Metadata.PartitionColumns);
        Assert.Equal("CREATEDON", partition.Name);
        Assert.Contains(platformIndex.Metadata.DataCompression, c => c.PartitionNumber == 1 && c.Compression == "PAGE");
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

        var smoModel = factory.Create(model, decisions, profile: snapshot, options: smoOptions);

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
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: smoOptions);

        var userTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_U_USER", StringComparison.OrdinalIgnoreCase));
        var uniqueIndex = userTable.Indexes.Single(i => i.Name.Equals("UX_User_Email", StringComparison.OrdinalIgnoreCase));
        Assert.True(uniqueIndex.IsUnique);
    }

    [Fact]
    public void Build_handles_duplicate_logical_entity_names_across_modules()
    {
        var inventoryCategory = CreateCategoryEntity("Inventory", "OSUSR_INV_CATEGORY");
        var supportCategory = inventoryCategory with
        {
            Module = ModuleName.Create("Support").Value,
            PhysicalName = TableName.Create("OSUSR_SUP_CATEGORY").Value
        };

        var productEntity = CreateProductEntity(inventoryCategory);

        var inventoryModule = ModuleModel.Create(
            ModuleName.Create("Inventory").Value,
            isSystemModule: false,
            isActive: true,
            new[] { inventoryCategory }).Value;
        var supportModule = ModuleModel.Create(
            ModuleName.Create("Support").Value,
            isSystemModule: false,
            isActive: true,
            new[] { supportCategory }).Value;
        var catalogModule = ModuleModel.Create(
            ModuleName.Create("Catalog").Value,
            isSystemModule: false,
            isActive: true,
            new[] { productEntity }).Value;

        var model = OsmModel.Create(DateTime.UtcNow, new[] { inventoryModule, supportModule, catalogModule }).Value;

        var fkCoordinate = new ColumnCoordinate(
            productEntity.Schema,
            productEntity.PhysicalName,
            ColumnName.Create("CATEGORYID").Value);
        var foreignKeyDecision = ForeignKeyDecision.Create(fkCoordinate, createConstraint: true, ImmutableArray<string>.Empty);

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty.Add(fkCoordinate, foreignKeyDecision),
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty);

        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);

        var smoModel = factory.Create(model, decisions, options: smoOptions);

        var categoryTables = smoModel.Tables
            .Where(t => t.LogicalName.Equals("Category", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(2, categoryTables.Length);

        var productTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_CAT_PRODUCT", StringComparison.OrdinalIgnoreCase));
        Assert.Single(productTable.ForeignKeys);
        var foreignKey = productTable.ForeignKeys[0];
        Assert.Equal("OSUSR_INV_CATEGORY", foreignKey.ReferencedTable);
        Assert.Equal("Category", foreignKey.ReferencedLogicalTable);
    }

    [Fact]
    public void Build_uses_supplemental_entities_for_missing_foreign_key_targets()
    {
        var module = ModuleName.Create("Audit").Value;
        var logical = EntityName.Create("AuditLog").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_AUDIT_LOG").Value;

        var idAttribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;

        var reference = AttributeReference.Create(
            isReference: true,
            targetEntityId: null,
            targetEntity: EntityName.Create("User").Value,
            targetPhysicalName: TableName.Create("OSUSR_U_USER").Value,
            deleteRuleCode: "Protect",
            hasDatabaseConstraint: true).Value;

        var userIdAttribute = AttributeModel.Create(
            AttributeName.Create("UserId").Value,
            ColumnName.Create("USERID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: reference).Value;

        var auditEntity = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { idAttribute, userIdAttribute }).Value;

        var auditModule = ModuleModel.Create(module, isSystemModule: false, isActive: true, new[] { auditEntity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { auditModule }).Value;

        var columnCoordinate = new ColumnCoordinate(schema, table, userIdAttribute.ColumnName);
        var fkDecision = ForeignKeyDecision.Create(columnCoordinate, createConstraint: true, ImmutableArray<string>.Empty);

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty.Add(columnCoordinate, fkDecision),
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty);

        var factory = new SmoModelFactory();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var supplemental = ImmutableArray.Create(OutSystemsInternalModel.Users);

        var smoModel = factory.Create(
            model,
            decisions,
            options: options,
            supplementalEntities: supplemental);

        var tableDefinition = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_AUDIT_LOG", StringComparison.OrdinalIgnoreCase));
        var foreignKey = Assert.Single(tableDefinition.ForeignKeys);
        Assert.Equal("OSUSR_U_USER", foreignKey.ReferencedTable);
        Assert.Equal("User", foreignKey.ReferencedLogicalTable);
    }

    private static EntityModel CreateCategoryEntity(string moduleName, string physicalName)
    {
        var module = ModuleName.Create(moduleName).Value;
        var logical = EntityName.Create("Category").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create(physicalName).Value;

        var id = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None).Value;

        var name = AttributeModel.Create(
            AttributeName.Create("Name").Value,
            ColumnName.Create("NAME").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None,
            length: 50).Value;

        return EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { id, name }).Value;
    }

    private static EntityModel CreateProductEntity(EntityModel category)
    {
        var module = ModuleName.Create("Catalog").Value;
        var logical = EntityName.Create("Product").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_CAT_PRODUCT").Value;

        var id = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None).Value;

        var reference = AttributeReference.Create(
            isReference: true,
            targetEntityId: 1,
            targetEntity: category.LogicalName,
            targetPhysicalName: category.PhysicalName,
            deleteRuleCode: "Protect",
            hasDatabaseConstraint: true).Value;

        var categoryId = AttributeModel.Create(
            AttributeName.Create("CategoryId").Value,
            ColumnName.Create("CATEGORYID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: reference).Value;

        return EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { id, categoryId }).Value;
    }
}
