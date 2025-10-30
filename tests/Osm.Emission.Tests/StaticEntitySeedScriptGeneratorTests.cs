using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Model.Emission;
using Osm.Domain.ValueObjects;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;

namespace Osm.Emission.Tests;

public class StaticEntitySeedScriptGeneratorTests
{
    [Fact]
    public void Generate_ProducesMergeBlocksForEachRow()
    {
        var definition = new StaticEntitySeedTableDefinition(
            Module: "TestModule",
            LogicalName: "Status",
            Schema: "dbo",
            PhysicalName: "OSUSR_TEST_STATUS",
            EffectiveName: "OSUSR_TEST_STATUS",
            Columns: ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("Name", "NAME", "NAME", "Text", 50, null, null, IsPrimaryKey: false, IsIdentity: false),
                new StaticEntitySeedColumn("IsActive", "ISACTIVE", "ISACTIVE", "Boolean", null, null, null, IsPrimaryKey: false, IsIdentity: false)));

        var rows = ImmutableArray.Create(
            StaticEntityRow.Create(new object?[] { 1, "Active", true }),
            StaticEntityRow.Create(new object?[] { 2, "Inactive", false }));

        var data = ImmutableArray.Create(StaticEntityTableData.Create(definition, rows));
        var generator = CreateGenerator();

        var script = generator.Generate(data, StaticSeedSynchronizationMode.NonDestructive);

        Assert.Contains("MERGE INTO [dbo].[OSUSR_TEST_STATUS] AS Target", script, StringComparison.Ordinal);
        Assert.Contains("VALUES\n        (1, N'Active', 1),\n        (2, N'Inactive', 0)", script, StringComparison.Ordinal);
        Assert.Contains("Target.[NAME] = Source.[NAME]", script, StringComparison.Ordinal);
        Assert.Contains("VALUES (Source.[ID], Source.[NAME], Source.[ISACTIVE])", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_UsesEffectiveTableNameWhenOverridden()
    {
        var definition = new StaticEntitySeedTableDefinition(
            Module: "AppCore",
            LogicalName: "City",
            Schema: "dbo",
            PhysicalName: "OSUSR_DEF_CITY",
            EffectiveName: "City",
            Columns: ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: true)));

        var rows = ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1 }));
        var data = ImmutableArray.Create(StaticEntityTableData.Create(definition, rows));
        var generator = CreateGenerator();

        var script = generator.Generate(data, StaticSeedSynchronizationMode.NonDestructive);

        Assert.Contains("-- Target: dbo.City", script, StringComparison.Ordinal);
        Assert.Contains("MERGE INTO [dbo].[City] AS Target", script, StringComparison.Ordinal);
        Assert.Contains("ON Target.[ID] = Source.[ID]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithAuthoritativeMode_AppendsDeleteClause()
    {
        var definition = new StaticEntitySeedTableDefinition(
            Module: "TestModule",
            LogicalName: "Status",
            Schema: "dbo",
            PhysicalName: "OSUSR_TEST_STATUS",
            EffectiveName: "OSUSR_TEST_STATUS",
            Columns: ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("Name", "NAME", "NAME", "Text", 50, null, null, IsPrimaryKey: false, IsIdentity: false)));

        var rows = ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "Active" }));
        var data = ImmutableArray.Create(StaticEntityTableData.Create(definition, rows));
        var generator = CreateGenerator();

        var script = generator.Generate(data, StaticSeedSynchronizationMode.Authoritative);

        Assert.Contains("WHEN NOT MATCHED BY SOURCE THEN DELETE;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithValidateThenApplyMode_AddsDriftGuards()
    {
        var definition = new StaticEntitySeedTableDefinition(
            Module: "TestModule",
            LogicalName: "Status",
            Schema: "dbo",
            PhysicalName: "OSUSR_TEST_STATUS",
            EffectiveName: "OSUSR_TEST_STATUS",
            Columns: ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("Name", "NAME", "NAME", "Text", 50, null, null, IsPrimaryKey: false, IsIdentity: false)));

        var rows = ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "Active" }));
        var data = ImmutableArray.Create(StaticEntityTableData.Create(definition, rows));
        var generator = CreateGenerator();

        var script = generator.Generate(data, StaticSeedSynchronizationMode.ValidateThenApply);

        Assert.Contains("IF EXISTS (", script, StringComparison.Ordinal);
        Assert.Contains(
            "THROW 50000, 'Static entity seed data drift detected for TestModule::Status (dbo.OSUSR_TEST_STATUS).', 1;",
            script,
            StringComparison.Ordinal);
        Assert.Contains("FROM [dbo].[OSUSR_TEST_STATUS] AS Existing", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithValidateThenApplyModeAndNoRows_GuardsAgainstExistingData()
    {
        var definition = new StaticEntitySeedTableDefinition(
            Module: "TestModule",
            LogicalName: "Status",
            Schema: "dbo",
            PhysicalName: "OSUSR_TEST_STATUS",
            EffectiveName: "OSUSR_TEST_STATUS",
            Columns: ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false)));

        var data = ImmutableArray.Create(StaticEntityTableData.Create(definition, Array.Empty<StaticEntityRow>()));
        var generator = CreateGenerator();

        var script = generator.Generate(data, StaticSeedSynchronizationMode.ValidateThenApply);

        Assert.Contains(
            "IF EXISTS (SELECT 1 FROM [dbo].[OSUSR_TEST_STATUS])",
            script,
            StringComparison.Ordinal);
        Assert.Contains("-- No data rows were returned for this static entity; MERGE statement omitted.", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitionBuilder_UsesEmissionSnapshotFiltering()
    {
        var identifier = CreateAttribute("Id", "ID", isIdentifier: true);
        var name = CreateAttribute("Name", "NAME");
        var inactive = CreateAttribute("IsDeleted", "ISDELETED", isActive: false);
        var presentButInactive = CreateAttribute("LegacyId", "LEGACYID", isIdentifier: true, presentButInactive: true);
        var computed = CreateAttribute(
            "Computed",
            "COMPUTED",
            onDisk: AttributeOnDiskMetadata.Create(null, null, null, null, null, null, null, isComputed: true, computedDefinition: "1", defaultDefinition: null));

        var entity = CreateEntity(
            moduleName: "Sales",
            logicalName: "Customer",
            physicalName: "OSUSR_SALES_CUSTOMER",
            schema: "sales",
            identifier,
            name,
            inactive,
            presentButInactive,
            computed);

        var module = CreateModule("Sales", entity);
        var model = CreateModel(module);
        var snapshot = EntityEmissionSnapshot.Create("Sales", entity);

        var definitions = StaticEntitySeedDefinitionBuilder.Build(model, NamingOverrideOptions.Empty);
        var definition = Assert.Single(definitions);

        var expectedColumns = snapshot.EmittableAttributes
            .Where(attribute => !(attribute.OnDisk.IsComputed ?? false))
            .Select(attribute => attribute.ColumnName.Value)
            .ToArray();

        Assert.Equal(expectedColumns, definition.Columns.Select(column => column.ColumnName));
    }

    private static StaticEntitySeedScriptGenerator CreateGenerator()
    {
        var literalFormatter = new SqlLiteralFormatter();
        var sqlBuilder = new StaticSeedSqlBuilder(literalFormatter);
        var templateService = new StaticEntitySeedTemplateService();
        return new StaticEntitySeedScriptGenerator(templateService, sqlBuilder);
    }

    private static AttributeModel CreateAttribute(
        string logicalName,
        string columnName,
        bool isIdentifier = false,
        bool isActive = true,
        bool presentButInactive = false,
        AttributeOnDiskMetadata? onDisk = null)
    {
        var reality = new AttributeReality(null, null, null, null, presentButInactive);
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: isActive,
            reality: reality,
            metadata: AttributeMetadata.Empty,
            onDisk: onDisk ?? AttributeOnDiskMetadata.Empty).Value;
    }

    private static EntityModel CreateEntity(
        string moduleName,
        string logicalName,
        string physicalName,
        string schema,
        params AttributeModel[] attributes)
    {
        return EntityModel.Create(
            new ModuleName(moduleName),
            new EntityName(logicalName),
            new TableName(physicalName),
            new SchemaName(schema),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: attributes).Value;
    }

    private static ModuleModel CreateModule(string moduleName, params EntityModel[] entities)
        => ModuleModel.Create(new ModuleName(moduleName), isSystemModule: false, isActive: true, entities: entities).Value;

    private static OsmModel CreateModel(params ModuleModel[] modules)
        => OsmModel.Create(DateTime.UtcNow, modules).Value;
}
