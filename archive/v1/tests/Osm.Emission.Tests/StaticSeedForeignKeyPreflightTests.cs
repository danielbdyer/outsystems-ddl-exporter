using System;
using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Emission.Seeds;

namespace Osm.Emission.Tests;

public sealed class StaticSeedForeignKeyPreflightTests
{
    [Fact]
    public void Analyze_WhenParentsPresent_ReturnsEmpty()
    {
        var (model, parent, child) = CreateParentChildModel();
        var ordered = ImmutableArray.Create(parent, child);

        var result = StaticSeedForeignKeyPreflight.Analyze(ordered, model);

        Assert.False(result.HasFindings);
        Assert.Empty(result.MissingParents);
        Assert.Empty(result.OrderingViolations);
    }

    [Fact]
    public void Analyze_WhenParentMissing_FlagsOrphan()
    {
        var (model, _, child) = CreateParentChildModel();
        var ordered = ImmutableArray.Create(child);

        var result = StaticSeedForeignKeyPreflight.Analyze(ordered, model);

        var issue = Assert.Single(result.MissingParents);
        Assert.Equal(StaticSeedForeignKeyIssueKind.MissingParent, issue.Kind);
        Assert.Equal("dbo", issue.ChildSchema);
        Assert.Equal("OSUSR_SAMPLE_CHILD", issue.ChildTable);
        Assert.True(result.OrderingViolations.IsDefaultOrEmpty);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void Analyze_WhenParentAfterChild_FlagsOrderingViolation()
    {
        var (model, parent, child) = CreateParentChildModel();
        var ordered = ImmutableArray.Create(child, parent);

        var result = StaticSeedForeignKeyPreflight.Analyze(ordered, model);

        var issue = Assert.Single(result.OrderingViolations);
        Assert.Equal(StaticSeedForeignKeyIssueKind.ParentAfterChild, issue.Kind);
        Assert.Equal(1, issue.ParentOrder);
        Assert.Equal(0, issue.ChildOrder);
        Assert.True(result.MissingParents.IsDefaultOrEmpty);
        Assert.True(result.HasFindings);
    }

    private static (OsmModel Model, StaticEntityTableData Parent, StaticEntityTableData Child) CreateParentChildModel()
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

        var parentTable = new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty);
        var childTable = new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty);

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
        return (model, parentTable, childTable);
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
