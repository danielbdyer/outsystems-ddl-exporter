using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using SmoIndex = Microsoft.SqlServer.Management.Smo.Index;
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
    [Fact]
    public void Build_applies_custom_index_naming_prefixes()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var factory = new SmoModelFactory();
        var defaultOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var format = SmoFormatOptions.Default.WithIndexNaming(new IndexNamingOptions(
            PrimaryKeyPrefix: "PKX",
            UniqueIndexPrefix: "UX",
            NonUniqueIndexPrefix: "IX",
            ForeignKeyPrefix: "FKX"));
        var options = defaultOptions.WithFormat(format);

        var smoModel = factory.Create(model, decisions, profile: snapshot, options: options);

        var customerTable = smoModel.Tables.Single(t => t.LogicalName.Equals("Customer", StringComparison.Ordinal));

        var primaryKey = Assert.Single(customerTable.Indexes.Where(i => i.IsPrimaryKey));
        Assert.StartsWith("PKX_", primaryKey.Name, StringComparison.Ordinal);

        var uniqueIndex = customerTable.Indexes.Single(i => i.IsUnique && !i.IsPrimaryKey);
        Assert.StartsWith("UX_", uniqueIndex.Name, StringComparison.Ordinal);

        var foreignKey = Assert.Single(customerTable.ForeignKeys);
        Assert.StartsWith("FKX_", foreignKey.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_respects_policy_decisions_for_edge_case_fixture()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: options);

        var columnLookup = BuildColumnLookup(smoModel);
        var indexLookup = BuildIndexLookup(smoModel);
        var foreignKeyLookup = BuildForeignKeyLookup(smoModel);

        var emailCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ABC_CUSTOMER"), new ColumnName("EMAIL"));
        Assert.True(decisions.Nullability.TryGetValue(emailCoordinate, out var emailDecision));
        Assert.True(emailDecision.MakeNotNull);
        var emailColumn = columnLookup[BuildColumnKey(emailCoordinate)];
        Assert.False(emailColumn.Nullable);
        Assert.Equal(SqlDataType.NVarChar, emailColumn.DataType.SqlDataType);
        Assert.Equal(255, emailColumn.DataType.MaximumLength);

        var cityCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ABC_CUSTOMER"), new ColumnName("CITYID"));
        Assert.True(decisions.Nullability.TryGetValue(cityCoordinate, out var cityDecision));
        Assert.True(cityDecision.MakeNotNull);
        var cityColumn = columnLookup[BuildColumnKey(cityCoordinate)];
        Assert.False(cityColumn.Nullable);
        Assert.Equal(SqlDataType.BigInt, cityColumn.DataType.SqlDataType);

        Assert.True(decisions.ForeignKeys.TryGetValue(cityCoordinate, out var cityForeignKeyDecision));
        Assert.True(cityForeignKeyDecision.CreateConstraint);
        Assert.True(foreignKeyLookup.TryGetValue(BuildColumnKey(cityCoordinate), out var cityForeignKeys));
        var cityForeignKey = Assert.Single(cityForeignKeys);
        Assert.Equal("OSUSR_DEF_CITY", cityForeignKey.ReferencedTable);
        Assert.Equal("dbo", cityForeignKey.ReferencedSchema);
        Assert.Equal("City", cityForeignKey.ReferencedLogicalTable);
        Assert.Equal(ForeignKeyAction.NoAction, cityForeignKey.DeleteAction);
        Assert.False(cityForeignKey.IsNoCheck);

        var triggeredCoordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_XYZ_JOBRUN"), new ColumnName("TRIGGEREDBYUSERID"));
        Assert.True(decisions.ForeignKeys.TryGetValue(triggeredCoordinate, out var triggeredDecision));
        Assert.False(triggeredDecision.CreateConstraint);
        var triggeredColumn = columnLookup[BuildColumnKey(triggeredCoordinate)];
        Assert.True(triggeredColumn.Nullable);
        Assert.False(foreignKeyLookup.ContainsKey(BuildColumnKey(triggeredCoordinate)));

        var emailIndexCoordinate = new IndexCoordinate(new SchemaName("dbo"), new TableName("OSUSR_ABC_CUSTOMER"), new IndexName("IDX_CUSTOMER_EMAIL"));
        Assert.True(decisions.UniqueIndexes.TryGetValue(emailIndexCoordinate, out var emailIndexDecision));
        Assert.True(emailIndexDecision.EnforceUnique);
        var emailIndex = indexLookup[BuildIndexKey(emailIndexCoordinate)];
        Assert.True(emailIndex.IsUnique);
        Assert.Equal("[EMAIL] IS NOT NULL", emailIndex.Metadata.FilterDefinition);
        Assert.Equal(85, emailIndex.Metadata.FillFactor);
        Assert.True(emailIndex.Metadata.IgnoreDuplicateKey);
        var emailDataSpace = emailIndex.Metadata.DataSpace;
        Assert.NotNull(emailDataSpace);
        Assert.Equal("FG_Customers", emailDataSpace!.Name);
        Assert.Equal("ROWS_FILEGROUP", emailDataSpace.Type);

        var accountNumberCoordinate = new ColumnCoordinate(new SchemaName("billing"), new TableName("BILLING_ACCOUNT"), new ColumnName("ACCOUNTNUMBER"));
        var accountNumberColumn = columnLookup[BuildColumnKey(accountNumberCoordinate)];
        Assert.False(accountNumberColumn.Nullable);
        Assert.Equal(SqlDataType.VarChar, accountNumberColumn.DataType.SqlDataType);
        Assert.Equal(50, accountNumberColumn.DataType.MaximumLength);

        var accountNumberIndexCoordinate = new IndexCoordinate(new SchemaName("billing"), new TableName("BILLING_ACCOUNT"), new IndexName("IDX_BILLINGACCOUNT_ACCTNUM"));
        Assert.True(decisions.UniqueIndexes.TryGetValue(accountNumberIndexCoordinate, out var accountNumberDecision));
        Assert.True(accountNumberDecision.EnforceUnique);
        var accountNumberIndex = indexLookup[BuildIndexKey(accountNumberIndexCoordinate)];
        Assert.True(accountNumberIndex.IsUnique);

        var customerTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(customerTable.Columns, c => c.LogicalName.Equals("LegacyCode", StringComparison.Ordinal));

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(jobRunTable.ForeignKeys);
        var triggerDefinition = Assert.Single(jobRunTable.Triggers);
        Assert.Equal("TR_OSUSR_XYZ_JOBRUN_AUDIT", triggerDefinition.Name);
        Assert.True(triggerDefinition.IsDisabled);
    }

    [Fact]
    public void Create_aligns_nullability_uniques_and_foreign_keys_with_policy()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: options);

        foreach (var pair in decisions.Nullability.Where(kvp => kvp.Value.MakeNotNull))
        {
            var table = smoModel.Tables.Single(t =>
                t.Schema.Equals(pair.Key.Schema.Value, StringComparison.OrdinalIgnoreCase) &&
                t.Name.Equals(pair.Key.Table.Value, StringComparison.OrdinalIgnoreCase));
            var column = table.Columns.Single(c => c.Name.Equals(pair.Key.Column.Value, StringComparison.OrdinalIgnoreCase));
            Assert.False(column.Nullable);
        }

        foreach (var pair in decisions.UniqueIndexes)
        {
            var table = smoModel.Tables.Single(t =>
                t.Schema.Equals(pair.Key.Schema.Value, StringComparison.OrdinalIgnoreCase) &&
                t.Name.Equals(pair.Key.Table.Value, StringComparison.OrdinalIgnoreCase));
            var index = table.Indexes.Single(i => i.Name.Equals(pair.Key.Index.Value, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(pair.Value.EnforceUnique, index.IsUnique);
        }

        foreach (var pair in decisions.ForeignKeys.Where(kvp => kvp.Value.CreateConstraint))
        {
            var table = smoModel.Tables.Single(t =>
                t.Schema.Equals(pair.Key.Schema.Value, StringComparison.OrdinalIgnoreCase) &&
                t.Name.Equals(pair.Key.Table.Value, StringComparison.OrdinalIgnoreCase));
            var foreignKey = table.ForeignKeys.Single(fk => fk.Columns.Contains(pair.Key.Column.Value, StringComparer.OrdinalIgnoreCase));
            Assert.False(foreignKey.IsNoCheck);
        }
    }

    [Fact]
    public void Build_aligns_reference_column_types_with_target_identifiers()
    {
        var (model, _, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var module = model.Modules.First(m => m.Entities.Any(e => e.LogicalName.Value.Equals("Customer", StringComparison.Ordinal)));
        var customer = module.Entities.First(e => e.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var cityId = customer.Attributes.First(a => a.ColumnName.Value.Equals("CityId", StringComparison.OrdinalIgnoreCase));

        var mutatedCityId = cityId with
        {
            DataType = "Text",
            OnDisk = cityId.OnDisk with
            {
                SqlType = "nvarchar",
                MaxLength = 50,
                Precision = null,
                Scale = null,
            }
        };

        var updatedCustomer = customer with { Attributes = customer.Attributes.Replace(cityId, mutatedCityId) };
        var updatedModule = module with { Entities = module.Entities.Replace(customer, updatedCustomer) };
        var updatedModel = model with { Modules = model.Modules.Replace(module, updatedModule) };

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(updatedModel, snapshot, TighteningOptions.Default);

        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var smoModel = factory.Create(updatedModel, decisions, profile: snapshot, options: smoOptions);

        var customerTable = smoModel.Tables.Single(t => t.LogicalName.Equals("Customer", StringComparison.Ordinal));
        var cityColumn = customerTable.Columns.Single(c => c.LogicalName.Equals("CityId", StringComparison.Ordinal));

        Assert.Equal(SqlDataType.BigInt, cityColumn.DataType.SqlDataType);
    }

    [Fact]
    public void Build_maps_on_disk_default_and_check_constraints()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
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
        var smoColumn = smoTable.Columns.Single(c => c.LogicalName.Equals(updatedAttribute.LogicalName.Value, StringComparison.Ordinal));

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
    public void Build_uses_physical_identifiers_when_logical_names_differ()
    {
        var (model, _, _) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var module = model.Modules.First(m =>
            m.Entities.Any(e => e.LogicalName.Value.Equals("Customer", StringComparison.Ordinal)) &&
            m.Entities.Any(e => e.LogicalName.Value.Equals("City", StringComparison.Ordinal)));

        var customer = module.Entities.First(e => e.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var city = module.Entities.First(e => e.LogicalName.Value.Equals("City", StringComparison.Ordinal));

        const string CustomerIdPhysical = "CUSTOMER_ID_PHYSICAL";
        const string CustomerCityIdPhysical = "CUSTOMER_CITY_ID_PHYSICAL";
        const string CityIdPhysical = "CITY_IDENTIFIER_PHYSICAL";

        var customerIdAttribute = customer.Attributes.Single(a => a.LogicalName.Value.Equals("Id", StringComparison.Ordinal));
        var customerCityAttribute = customer.Attributes.Single(a => a.LogicalName.Value.Equals("CityId", StringComparison.Ordinal));

        var updatedCustomerId = customerIdAttribute with { ColumnName = new ColumnName(CustomerIdPhysical) };
        var updatedCustomerCity = customerCityAttribute with { ColumnName = new ColumnName(CustomerCityIdPhysical) };

        var customerAttributes = customer.Attributes
            .Replace(customerIdAttribute, updatedCustomerId)
            .Replace(customerCityAttribute, updatedCustomerCity);

        var customerIndexes = customer.Indexes
            .Select(index => index with
            {
                Columns = index.Columns
                    .Select(column => column.Column.Value.Equals(customerIdAttribute.ColumnName.Value, StringComparison.OrdinalIgnoreCase)
                        ? column with { Column = new ColumnName(CustomerIdPhysical) }
                        : column.Column.Value.Equals(customerCityAttribute.ColumnName.Value, StringComparison.OrdinalIgnoreCase)
                            ? column with { Column = new ColumnName(CustomerCityIdPhysical) }
                            : column)
                    .ToImmutableArray()
            })
            .ToImmutableArray();

        var updatedCustomer = customer with
        {
            Attributes = customerAttributes,
            Indexes = customerIndexes
        };

        var cityIdAttribute = city.Attributes.Single(a => a.LogicalName.Value.Equals("Id", StringComparison.Ordinal));
        var updatedCityId = cityIdAttribute with { ColumnName = new ColumnName(CityIdPhysical) };
        var cityAttributes = city.Attributes.Replace(cityIdAttribute, updatedCityId);

        var cityIndexes = city.Indexes
            .Select(index => index with
            {
                Columns = index.Columns
                    .Select(column => column.Column.Value.Equals(cityIdAttribute.ColumnName.Value, StringComparison.OrdinalIgnoreCase)
                        ? column with { Column = new ColumnName(CityIdPhysical) }
                        : column)
                    .ToImmutableArray()
            })
            .ToImmutableArray();

        var updatedCity = city with
        {
            Attributes = cityAttributes,
            Indexes = cityIndexes
        };

        var updatedEntities = module.Entities
            .Replace(customer, updatedCustomer)
            .Replace(city, updatedCity);

        var updatedModule = module with { Entities = updatedEntities };
        var updatedModel = model with { Modules = model.Modules.Replace(module, updatedModule) };

        var emptySnapshot = new ProfileSnapshot(
            ImmutableArray<ColumnProfile>.Empty,
            ImmutableArray<UniqueCandidateProfile>.Empty,
            ImmutableArray<CompositeUniqueCandidateProfile>.Empty,
            ImmutableArray<ForeignKeyReality>.Empty);

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(updatedModel, emptySnapshot, TighteningOptions.Default);

        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(updatedModel, decisions, profile: emptySnapshot, options: options);

        var customerTable = smoModel.Tables.Single(t => t.LogicalName.Equals("Customer", StringComparison.Ordinal));
        var idColumn = customerTable.Columns.Single(c => c.LogicalName.Equals("Id", StringComparison.Ordinal));
        var cityColumn = customerTable.Columns.Single(c => c.LogicalName.Equals("CityId", StringComparison.Ordinal));

        Assert.Equal(CustomerIdPhysical, idColumn.Name);
        Assert.Equal(CustomerCityIdPhysical, cityColumn.Name);

        var primaryKey = customerTable.Indexes.Single(i => i.IsPrimaryKey);
        Assert.Collection(primaryKey.Columns.OrderBy(c => c.Ordinal),
            col => Assert.Equal(CustomerIdPhysical, col.Name));

        var foreignKey = Assert.Single(customerTable.ForeignKeys);
        Assert.Equal(CustomerCityIdPhysical, Assert.Single(foreignKey.Columns));
        Assert.Equal(CityIdPhysical, Assert.Single(foreignKey.ReferencedColumns));

        var writer = new PerTableWriter();
        var script = writer.Generate(customerTable, options).Script;

        Assert.Contains($"[{CustomerIdPhysical}]", script, StringComparison.Ordinal);
        Assert.Contains($"[{CustomerCityIdPhysical}]", script, StringComparison.Ordinal);
        Assert.Contains($"[{CityIdPhysical}]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_matches_edge_case_fixture_scripts()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, profile: snapshot, options: options);
        var writer = new PerTableWriter();

        foreach (var table in smoModel.Tables)
        {
            var result = writer.Generate(table, options);
            var expectedPath = Path.Combine(
                FixtureFile.RepositoryRoot,
                "tests",
                "Fixtures",
                "emission",
                "edge-case",
                "Modules",
                table.Module,
                "Tables",
                $"{table.Schema}.{table.LogicalName}.sql");

            Assert.True(File.Exists(expectedPath), $"Expected fixture '{expectedPath}' to exist.");
            var expected = File.ReadAllText(expectedPath);
            Assert.Equal(Normalize(expected), Normalize(result.Script));
        }
    }

    private static Dictionary<string, SmoColumnDefinition> BuildColumnLookup(SmoModel smoModel)
        => smoModel.Tables
            .SelectMany(table => table.Columns.Select(column => (
                Key: BuildColumnKey(table.Schema, table.Name, column.Name),
                Column: column)))
            .ToDictionary(static pair => pair.Key, static pair => pair.Column);

    private static Dictionary<string, SmoIndexDefinition> BuildIndexLookup(SmoModel smoModel)
        => smoModel.Tables
            .SelectMany(table => table.Indexes.Select(index => (
                Key: BuildIndexKey(table.Schema, table.Name, index.Name),
                Index: index)))
            .ToDictionary(static pair => pair.Key, static pair => pair.Index);

    private static Dictionary<string, ImmutableArray<SmoForeignKeyDefinition>> BuildForeignKeyLookup(SmoModel smoModel)
        => smoModel.Tables
            .SelectMany(table => table.ForeignKeys.SelectMany(foreignKey => foreignKey.Columns.Select(column => (
                Key: BuildColumnKey(table.Schema, table.Name, column),
                ForeignKey: foreignKey))))
            .GroupBy(static pair => pair.Key, static pair => pair.ForeignKey)
            .ToDictionary(static group => group.Key, static group => group.ToImmutableArray());

    private static string BuildColumnKey(ColumnCoordinate coordinate)
        => BuildColumnKey(coordinate.Schema.Value, coordinate.Table.Value, coordinate.Column.Value);

    private static string BuildColumnKey(string schema, string table, string column)
        => BuildKey(schema, table, column);

    private static string BuildIndexKey(IndexCoordinate coordinate)
        => BuildIndexKey(coordinate.Schema.Value, coordinate.Table.Value, coordinate.Index.Value);

    private static string BuildIndexKey(string schema, string table, string index)
        => BuildKey(schema, table, index);

    private static string BuildKey(string schema, string table, string name)
        => $"{schema.ToUpperInvariant()}|{table.ToUpperInvariant()}|{name.ToUpperInvariant()}";

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n").Trim();

    [Fact]
    public void Build_excludes_platform_auto_indexes_when_disabled()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
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
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var factory = new SmoModelFactory();
        var options = new SmoBuildOptions(
            "OutSystems",
            IncludePlatformAutoIndexes: true,
            EmitBareTableOnly: false,
            SanitizeModuleNames: true,
            ModuleParallelism: 1,
            NamingOverrides: NamingOverrideOptions.Empty,
            Format: SmoFormatOptions.Default,
            Header: PerTableHeaderOptions.Disabled);
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
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty.Add(fkCoordinate, productEntity.Module.Value),
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

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
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty.Add(columnCoordinate, auditEntity.Module.Value),
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

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

    [Fact]
    public void CreateSmoTables_materializes_detached_smo_objects()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var factory = new SmoModelFactory();
        var options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);

        var tables = factory.CreateSmoTables(model, decisions, snapshot, options);
        Assert.NotEmpty(tables);

        var customer = tables.Single(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        var pk = Assert.Single(customer.Indexes.Cast<SmoIndex>(), index => index.IndexKeyType == IndexKeyType.DriPrimaryKey);
        Assert.Equal("PK_Customer", pk.Name);

        var foreignKey = Assert.Single(customer.ForeignKeys.Cast<ForeignKey>());
        Assert.True(foreignKey.IsChecked);
        Assert.Equal("OSUSR_DEF_CITY", foreignKey.ReferencedTable);
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
