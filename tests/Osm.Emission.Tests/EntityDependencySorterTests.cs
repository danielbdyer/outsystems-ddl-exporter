using System;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Emission.Seeds;
using Xunit;

namespace Osm.Emission.Tests;

public sealed class EntityDependencySorterTests
{
    [Fact]
    public void SortByForeignKeys_ParentsPrecedeChildren()
    {
        var parentDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "OSUSR_SAMPLE_PARENT",
            "OSUSR_SAMPLE_PARENT",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));

        var childDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "OSUSR_SAMPLE_CHILD",
            "OSUSR_SAMPLE_CHILD",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty));

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

        var ordering = EntityDependencySorter.SortByForeignKeys(tables, model);

        Assert.True(ordering.TopologicalOrderingApplied);
        Assert.Equal(2, ordering.NodeCount);
        Assert.Equal(1, ordering.EdgeCount);
        Assert.Collection(
            ordering.Tables,
            first => Assert.Equal("Parent", first.Definition.LogicalName),
            second => Assert.Equal("Child", second.Definition.LogicalName));
    }

    [Fact]
    public void SortByForeignKeys_ReportsEdgesAfterMetadataEnrichment()
    {
        var parentDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "OSUSR_SAMPLE_PARENT",
            "OSUSR_SAMPLE_PARENT",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));

        var childDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "OSUSR_SAMPLE_CHILD",
            "OSUSR_SAMPLE_CHILD",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty));

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

        var childRelationshipWithoutColumns = RelationshipModel.Create(
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
                    Array.Empty<RelationshipActualConstraintColumn>())
            }).Value;

        var childRelationshipWithColumns = childRelationshipWithoutColumns with
        {
            ActualConstraints = ImmutableArray.Create(
                RelationshipActualConstraint.Create(
                    "FK_CHILD_PARENT",
                    referencedSchema: "dbo",
                    referencedTable: "OSUSR_SAMPLE_PARENT",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("PARENTID", "ParentId", "ID", "Id", 0) }))
        };

        var childEntityWithoutColumns = EntityModel.Create(
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
            relationships: new[] { childRelationshipWithoutColumns }).Value;

        var childEntityWithColumns = childEntityWithoutColumns with
        {
            Relationships = ImmutableArray.Create(childRelationshipWithColumns)
        };

        var modelWithoutColumns = OsmModel.Create(
            DateTime.UtcNow,
            new[]
            {
                ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { parentEntity, childEntityWithoutColumns }).Value
            }).Value;

        var modelWithColumns = OsmModel.Create(
            DateTime.UtcNow,
            new[]
            {
                ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { parentEntity, childEntityWithColumns }).Value
            }).Value;

        var orderingWithoutColumns = EntityDependencySorter.SortByForeignKeys(tables, modelWithoutColumns);
        Assert.Equal(0, orderingWithoutColumns.EdgeCount);

        var orderingWithColumns = EntityDependencySorter.SortByForeignKeys(tables, modelWithColumns);
        Assert.Equal(1, orderingWithColumns.EdgeCount);
    }

    [Fact]
    public void SortByForeignKeys_ReportsMissingEdgesWhenReferencedTableAbsent()
    {
        var childDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "OSUSR_SAMPLE_CHILD",
            "OSUSR_SAMPLE_CHILD",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty));

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

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { childEntity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var ordering = EntityDependencySorter.SortByForeignKeys(tables, model);

        Assert.False(ordering.TopologicalOrderingAttempted);
        Assert.Equal(1, ordering.MissingEdgeCount);
        Assert.Equal(0, ordering.EdgeCount);
        Assert.Single(ordering.Tables);
    }

    [Fact]
    public void SortByForeignKeys_DetectsCyclesAndAppliesFallback()
    {
        var parentDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "OSUSR_SAMPLE_PARENT",
            "OSUSR_SAMPLE_PARENT",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ChildId", "CHILDID", "ChildId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var childDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "OSUSR_SAMPLE_CHILD",
            "OSUSR_SAMPLE_CHILD",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty));

        var parentRelationship = RelationshipModel.Create(
            new AttributeName("ChildId"),
            new EntityName("Child"),
            new TableName("OSUSR_SAMPLE_CHILD"),
            deleteRuleCode: "NO_ACTION",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    "FK_PARENT_CHILD",
                    referencedSchema: "dbo",
                    referencedTable: "OSUSR_SAMPLE_CHILD",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("CHILDID", "ChildId", "ID", "Id", 0) })
            }).Value;

        var childRelationship = RelationshipModel.Create(
            new AttributeName("ParentId"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            deleteRuleCode: "NO_ACTION",
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

        var parentEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("ChildId", "CHILDID")
            },
            relationships: new[] { parentRelationship }).Value;

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
            relationships: new[] { childRelationship }).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { parentEntity, childEntity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var ordering = EntityDependencySorter.SortByForeignKeys(tables, model);

        Assert.True(ordering.CycleDetected);
        Assert.True(ordering.AlphabeticalFallbackApplied);
        Assert.False(ordering.TopologicalOrderingApplied);
        Assert.Equal(2, ordering.Tables.Length);
    }

    [Fact]
    public void SortByForeignKeys_ResolvesSanitizedEffectiveNames()
    {
        var parentDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "USR_SAMPLE_PARENT_SAN",
            "USR_SAMPLE_PARENT_SAN",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));

        var childDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "USR_SAMPLE_CHILD_SAN",
            "USR_SAMPLE_CHILD_SAN",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty));

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

        var parentOverride = NamingOverrideRule.Create("dbo", "OSUSR_SAMPLE_PARENT", null, null, "USR_SAMPLE_PARENT_SAN").Value;
        var childOverride = NamingOverrideRule.Create("dbo", "OSUSR_SAMPLE_CHILD", null, null, "USR_SAMPLE_CHILD_SAN").Value;
        var namingOverrides = NamingOverrideOptions.Create(new[] { parentOverride, childOverride }).Value;

        var ordering = EntityDependencySorter.SortByForeignKeys(tables, model, namingOverrides);

        Assert.True(ordering.TopologicalOrderingApplied);
        Assert.Equal(1, ordering.EdgeCount);
        Assert.Equal(0, ordering.MissingEdgeCount);
        Assert.Collection(
            ordering.Tables,
            first => Assert.Equal("Parent", first.Definition.LogicalName),
            second => Assert.Equal("Child", second.Definition.LogicalName));
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
