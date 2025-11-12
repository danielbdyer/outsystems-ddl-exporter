using System;
using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Emission.Seeds;
using Xunit;

namespace Osm.Emission.Tests;

public sealed class StaticEntityDependencySorterTests
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
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false)));

        var childDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "OSUSR_SAMPLE_CHILD",
            "OSUSR_SAMPLE_CHILD",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false)));

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

        var ordered = StaticEntityDependencySorter.SortByForeignKeys(tables, model);

        Assert.Collection(
            ordered,
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
