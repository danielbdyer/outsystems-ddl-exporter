using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Emission.Tests;

public sealed class DynamicEntityInsertGeneratorTests
{
    private static readonly SqlLiteralFormatter Formatter = new();

    [Fact]
    public void GenerateScripts_IncludesStaticSeedRowsAndSorts()
    {
        var definition = CreateDefinition("App", "dbo", "ENTITIES", "Entities", isIdentity: false);
        var datasetRows = ImmutableArray.Create(
            StaticEntityRow.Create(new object?[] { 2, "Beta" }),
            StaticEntityRow.Create(new object?[] { 3, "Gamma" }),
            StaticEntityRow.Create(new object?[] { 1, "Alpha" }),
            StaticEntityRow.Create(new object?[] { 2, "Beta" }));
        var dataset = new DynamicEntityDataset(ImmutableArray.Create(new StaticEntityTableData(definition, datasetRows)));
        var staticSeeds = ImmutableArray.Create(new StaticEntityTableData(
            definition,
            ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "Alpha" }))));

        var generator = new DynamicEntityInsertGenerator(Formatter);

        var scripts = generator.GenerateScripts(dataset, staticSeeds);

        Assert.Single(scripts);
        var script = scripts[0];
        Assert.Contains("INSERT INTO [dbo].[Entities] WITH (TABLOCK, CHECK_CONSTRAINTS)", script.Script, System.StringComparison.Ordinal);
        var alphaIndex = script.Script.IndexOf("(1, N'Alpha')", System.StringComparison.Ordinal);
        var betaIndex = script.Script.IndexOf("(2, N'Beta')", System.StringComparison.Ordinal);
        var gammaIndex = script.Script.IndexOf("(3, N'Gamma')", System.StringComparison.Ordinal);
        Assert.True(alphaIndex >= 0, "Expected batch row for key 1.");
        Assert.True(betaIndex > alphaIndex, "Expected rows ordered by primary key after deduplication.");
        Assert.True(gammaIndex > betaIndex, "Expected rows ordered by primary key after deduplication.");
    }

    [Fact]
    public void GenerateScripts_RespectsBatchSize()
    {
        var definition = CreateDefinition("App", "dbo", "BULK", "BulkEntities", isIdentity: true);
        var rows = Enumerable.Range(1, 12)
            .Select(i => StaticEntityRow.Create(new object?[] { i, $"Name-{i}" }))
            .ToImmutableArray();
        var dataset = new DynamicEntityDataset(ImmutableArray.Create(new StaticEntityTableData(definition, rows)));

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var options = new DynamicEntityInsertGenerationOptions(batchSize: 5);

        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty, options);

        Assert.Single(scripts);
        var script = scripts[0].Script;
        // Expect three INSERT batches due to batch size (5 + 5 + 2)
        var insertCount = script.Split("INSERT INTO", System.StringSplitOptions.RemoveEmptyEntries).Length - 1;
        Assert.Equal(3, insertCount);
        Assert.Contains("SET IDENTITY_INSERT [dbo].[BulkEntities] ON", script, System.StringComparison.Ordinal);
        Assert.Contains("SET IDENTITY_INSERT [dbo].[BulkEntities] OFF", script, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateScripts_BatchesLargeDatasetsWithoutRepeatedEnumeration()
    {
        var definition = CreateDefinition("App", "dbo", "LARGE", "LargeEntities", isIdentity: false);
        const int rowCount = 2345;
        const int batchSize = 500;

        var rows = Enumerable.Range(1, rowCount)
            .Select(i => StaticEntityRow.Create(new object?[] { i, $"Name-{i}" }))
            .ToImmutableArray();
        var dataset = new DynamicEntityDataset(ImmutableArray.Create(new StaticEntityTableData(definition, rows)));

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var options = new DynamicEntityInsertGenerationOptions(batchSize);

        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty, options);

        Assert.Single(scripts);
        var script = scripts[0].Script;
        var batchCount = script.Split("PRINT 'Applying batch", System.StringSplitOptions.RemoveEmptyEntries).Length - 1;
        var expectedBatches = (int)System.Math.Ceiling(rowCount / (double)batchSize);
        Assert.Equal(expectedBatches, batchCount);
    }

    [Fact]
    public void GenerateScripts_SortsTablesByModuleAndName()
    {
        var alpha = CreateDefinition("Alpha", "dbo", "ONE", "One", isIdentity: false);
        var beta = CreateDefinition("Beta", "dbo", "TWO", "Two", isIdentity: false);
        var dataset = new DynamicEntityDataset(ImmutableArray.Create(
            new StaticEntityTableData(beta, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "B" }))),
            new StaticEntityTableData(alpha, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, "A" })))));

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty);

        Assert.Equal(2, scripts.Length);
        Assert.Equal("Alpha", scripts[0].Definition.Module);
        Assert.Equal("Beta", scripts[1].Definition.Module);
    }

    [Fact]
    public void GenerateScripts_OrdersTablesByForeignKeyDependencies()
    {
        var parentDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "OSUSR_SAMPLE_PARENT",
            "OSUSR_SAMPLE_PARENT",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false)));

        var childDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "OSUSR_SAMPLE_CHILD",
            "OSUSR_SAMPLE_CHILD",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "Identifier", null, null, null, IsPrimaryKey: false, IsIdentity: false)));

        var dataset = new DynamicEntityDataset(ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, 1 }))),
            new StaticEntityTableData(parentDefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1 })))));

        var parentEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[] { CreateAttribute("Id", "ID", isIdentifier: true) }).Value;

        var relationship = RelationshipModel.Create(
            new AttributeName("ParentId"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    "FK_CHILD_PARENT",
                    referencedSchema: "dbo",
                    referencedTable: "OSUSR_SAMPLE_PARENT",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("PARENTID", "ParentId", "ID", "Id", 0) })
            }).Value;

        var childEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Child"),
            new TableName("OSUSR_SAMPLE_CHILD"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("ParentId", "PARENTID")
            },
            relationships: new[] { relationship }).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { parentEntity, childEntity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty, model: model);

        Assert.Equal(2, scripts.Length);
        Assert.Equal("Parent", scripts[0].Definition.LogicalName);
        Assert.Equal("Child", scripts[1].Definition.LogicalName);
    }

    [Fact]
    public void GenerateScripts_EmitsSelfReferencingParentsBeforeChildren()
    {
        var definition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Hierarchy",
            "dbo",
            "OSUSR_SAMPLE_HIERARCHY",
            "OSUSR_SAMPLE_HIERARCHY",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "INT", null, null, null, IsPrimaryKey: false, IsIdentity: false),
                new StaticEntitySeedColumn("Name", "NAME", "Name", "NVARCHAR", 50, null, null, IsPrimaryKey: false, IsIdentity: false)));

        var rows = ImmutableArray.Create(
            StaticEntityRow.Create(new object?[] { 5, 10, "Child" }),
            StaticEntityRow.Create(new object?[] { 10, null, "Root" }));

        var dataset = new DynamicEntityDataset(ImmutableArray.Create(new StaticEntityTableData(definition, rows)));

        var relationship = RelationshipModel.Create(
            new AttributeName("ParentId"),
            new EntityName("Hierarchy"),
            new TableName("OSUSR_SAMPLE_HIERARCHY"),
            deleteRuleCode: "Ignore",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    "FK_HIERARCHY_PARENT",
                    referencedSchema: "dbo",
                    referencedTable: "OSUSR_SAMPLE_HIERARCHY",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("PARENTID", "ParentId", "ID", "Id", 0) })
            }).Value;

        var entity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Hierarchy"),
            new TableName("OSUSR_SAMPLE_HIERARCHY"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("ParentId", "PARENTID"),
                CreateAttribute("Name", "NAME")
            },
            relationships: new[] { relationship }).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { entity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty, model: model);

        Assert.Single(scripts);
        var script = scripts[0].Script;
        var rootIndex = script.IndexOf("(10, NULL, N'Root')", StringComparison.Ordinal);
        var childIndex = script.IndexOf("(5, 10, N'Child')", StringComparison.Ordinal);

        Assert.True(rootIndex >= 0, "Expected script to include the root row.");
        Assert.True(childIndex >= 0, "Expected script to include the child row.");
        Assert.True(rootIndex < childIndex, "Expected parent row to precede child row for self-referencing hierarchy.");
    }

    private static StaticEntitySeedTableDefinition CreateDefinition(
        string module,
        string schema,
        string physicalName,
        string logicalName,
        bool isIdentity)
    {
        var columns = ImmutableArray.Create(
            new StaticEntitySeedColumn(
                LogicalName: "Id",
                ColumnName: "Id",
                EmissionName: "Id",
                DataType: "int",
                Length: null,
                Precision: null,
                Scale: null,
                IsPrimaryKey: true,
                IsIdentity: isIdentity),
            new StaticEntitySeedColumn(
                LogicalName: "Name",
                ColumnName: "Name",
                EmissionName: "Name",
                DataType: "nvarchar",
                Length: 50,
                Precision: null,
                Scale: null,
                IsPrimaryKey: false,
                IsIdentity: false));

        return new StaticEntitySeedTableDefinition(
            module,
            logicalName,
            schema,
            physicalName,
            logicalName,
            columns);
    }

    private static AttributeModel CreateAttribute(string logicalName, string columnName, bool isIdentifier = false)
    {
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: true,
            reality: new AttributeReality(null, null, null, null, IsPresentButInactive: false),
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
    }
}
