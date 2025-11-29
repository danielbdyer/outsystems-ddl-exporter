using System;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Xunit;

namespace Osm.Emission.Tests;

public sealed class PhasedDynamicEntityInsertGeneratorTests
{
    private static readonly SqlLiteralFormatter Formatter = new();

    [Fact]
    public void Generate_ProducesMergeAndSetBasedUpdateForNullableFkCycles()
    {
        var tableADefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "A",
            "dbo",
            "OSUSR_SAMPLE_A",
            "A",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("BId", "BID", "BID", "INT", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tableBDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "B",
            "dbo",
            "OSUSR_SAMPLE_B",
            "B",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("AId", "AID", "AId", "INT", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: true)));

        var dataset = new DynamicEntityDataset(
            ImmutableArray.Create(
                new StaticEntityTableData(tableADefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, 2 }))),
                new StaticEntityTableData(tableBDefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 2, 1 })))));

        var entityA = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("A"),
            new TableName("OSUSR_SAMPLE_A"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("BId", "BID", isMandatory: true)
            },
            relationships: new[]
            {
                CreateRelationship(
                    viaAttribute: "BId",
                    targetEntity: "B",
                    targetTable: "OSUSR_SAMPLE_B",
                    foreignKeyName: "FK_A_B",
                    sourceColumn: "BID",
                    targetColumn: "ID")
            }).Value;

        var entityB = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("B"),
            new TableName("OSUSR_SAMPLE_B"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("AId", "AID")
            },
            relationships: new[]
            {
                CreateRelationship(
                    viaAttribute: "AId",
                    targetEntity: "A",
                    targetTable: "OSUSR_SAMPLE_A",
                    foreignKeyName: "FK_B_A",
                    sourceColumn: "AID",
                    targetColumn: "ID")
            }).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { entityA, entityB }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var generator = new PhasedDynamicEntityInsertGenerator(Formatter);
        var script = generator.Generate(dataset, model).ToScript();

        Assert.Contains("PhaseOneSource", script, StringComparison.Ordinal);
        Assert.Contains("MERGE INTO [dbo].[OSUSR_SAMPLE_B] AS Target", script, StringComparison.Ordinal);
        Assert.Contains("USING PhaseOneSource AS Source", script, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN 1 = 0 THEN SourceRows.[AId] ELSE NULL END AS [AId]", script, StringComparison.Ordinal);
        Assert.Contains("SET [AId] = Source.[AId]", script, StringComparison.Ordinal);
        Assert.Contains("JOIN SourceRows AS Source", script, StringComparison.Ordinal);

        var mergeCount = script.Split("MERGE INTO", StringSplitOptions.RemoveEmptyEntries).Length - 1;
        Assert.Equal(2, mergeCount); // Both tables emitted in a single script
    }

    [Fact]
    public void Generate_RespectsManualCycleOrderingOverrides()
    {
        var parentDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "OSUSR_SAMPLE_PARENT",
            "Parent",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("LatestAuditId", "LATESTAUDITID", "LatestAuditId", "INT", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: true)));

        var auditDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Audit",
            "dbo",
            "OSUSR_SAMPLE_AUDIT",
            "Audit",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "INT", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var dataset = new DynamicEntityDataset(
            ImmutableArray.Create(
                new StaticEntityTableData(parentDefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, 2 }))),
                new StaticEntityTableData(auditDefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 2, 1 })))));

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
                CreateAttribute("LatestAuditId", "LATESTAUDITID")
            },
            relationships: new[]
            {
                CreateRelationship(
                    viaAttribute: "LatestAuditId",
                    targetEntity: "Audit",
                    targetTable: "OSUSR_SAMPLE_AUDIT",
                    foreignKeyName: "FK_PARENT_AUDIT",
                    sourceColumn: "LATESTAUDITID",
                    targetColumn: "ID")
            }).Value;

        var auditEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Audit"),
            new TableName("OSUSR_SAMPLE_AUDIT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("ParentId", "PARENTID", isMandatory: true)
            },
            relationships: new[]
            {
                CreateRelationship(
                    viaAttribute: "ParentId",
                    targetEntity: "Parent",
                    targetTable: "OSUSR_SAMPLE_PARENT",
                    foreignKeyName: "FK_AUDIT_PARENT",
                    sourceColumn: "PARENTID",
                    targetColumn: "ID")
            }).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { parentEntity, auditEntity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var parentOrdering = TableOrdering.Create(parentDefinition.PhysicalName!, 100).Value;
        var auditOrdering = TableOrdering.Create(auditDefinition.PhysicalName!, 200).Value;
        var circularDependencyOptions = CircularDependencyOptions.Create(
            ImmutableArray.Create(AllowedCycle.Create(ImmutableArray.Create(parentOrdering, auditOrdering)).Value),
            strictMode: false).Value;

        var generator = new PhasedDynamicEntityInsertGenerator(Formatter);
        var script = generator.Generate(dataset, model, circularDependencyOptions: circularDependencyOptions).ToScript();

        Assert.DoesNotContain("Phase 2", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PhaseOneSource", script, StringComparison.OrdinalIgnoreCase);

        var parentIndex = script.IndexOf("MERGE INTO [dbo].[OSUSR_SAMPLE_PARENT] AS Target", StringComparison.Ordinal);
        var auditIndex = script.IndexOf("MERGE INTO [dbo].[OSUSR_SAMPLE_AUDIT] AS Target", StringComparison.Ordinal);

        Assert.True(parentIndex >= 0 && auditIndex > parentIndex, "Parent MERGE should precede Audit MERGE per manual ordering");
    }

    [Fact]
    public void Generate_PhasesOnlyCycleParticipants()
    {
        var tableADefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "A",
            "dbo",
            "OSUSR_SAMPLE_A",
            "A",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("BId", "BID", "BID", "INT", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tableBDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "B",
            "dbo",
            "OSUSR_SAMPLE_B",
            "B",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("AId", "AID", "AId", "INT", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: true)));

        var tableCDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "C",
            "dbo",
            "OSUSR_SAMPLE_C",
            "C",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "ID", "INT", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));

        var dataset = new DynamicEntityDataset(
            ImmutableArray.Create(
                new StaticEntityTableData(tableADefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1, 2 }))),
                new StaticEntityTableData(tableBDefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 2, 1 }))),
                new StaticEntityTableData(tableCDefinition, ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 3 })))));

        var entityA = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("A"),
            new TableName("OSUSR_SAMPLE_A"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("BId", "BID", isMandatory: true)
            },
            relationships: new[]
            {
                CreateRelationship(
                    viaAttribute: "BId",
                    targetEntity: "B",
                    targetTable: "OSUSR_SAMPLE_B",
                    foreignKeyName: "FK_A_B",
                    sourceColumn: "BID",
                    targetColumn: "ID")
            }).Value;

        var entityB = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("B"),
            new TableName("OSUSR_SAMPLE_B"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("AId", "AID")
            },
            relationships: new[]
            {
                CreateRelationship(
                    viaAttribute: "AId",
                    targetEntity: "A",
                    targetTable: "OSUSR_SAMPLE_A",
                    foreignKeyName: "FK_B_A",
                    sourceColumn: "AID",
                    targetColumn: "ID")
            }).Value;

        var entityC = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("C"),
            new TableName("OSUSR_SAMPLE_C"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true)
            },
            relationships: Array.Empty<RelationshipModel>()).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { entityA, entityB, entityC }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var generator = new PhasedDynamicEntityInsertGenerator(Formatter);
        var script = generator.Generate(dataset, model).ToScript();

        var phaseOneSourceOccurrences = script.Split("PhaseOneSource", StringSplitOptions.RemoveEmptyEntries).Length - 1;
        Assert.Equal(1, phaseOneSourceOccurrences);

        Assert.Contains("UPDATE nullable FKs: dbo.OSUSR_SAMPLE_B", script, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE nullable FKs: dbo.OSUSR_SAMPLE_C", script, StringComparison.Ordinal);
        Assert.Contains("MERGE INTO [dbo].[OSUSR_SAMPLE_C] AS Target\nUSING SourceRows AS Source", script, StringComparison.Ordinal);
    }

    private static AttributeModel CreateAttribute(string logicalName, string columnName, bool isIdentifier = false, bool isMandatory = false)
    {
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isMandatory || isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: true,
            reality: new AttributeReality(null, null, null, null, IsPresentButInactive: false),
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
    }

    private static RelationshipModel CreateRelationship(
        string viaAttribute,
        string targetEntity,
        string targetTable,
        string foreignKeyName,
        string sourceColumn,
        string targetColumn)
    {
        var constraint = RelationshipActualConstraint.Create(
            foreignKeyName,
            referencedSchema: "dbo",
            referencedTable: targetTable,
            onDeleteAction: "NO_ACTION",
            onUpdateAction: "NO_ACTION",
            new[] { RelationshipActualConstraintColumn.Create(sourceColumn, viaAttribute, targetColumn, "Id", 0) });

        return RelationshipModel.Create(
            new AttributeName(viaAttribute),
            new EntityName(targetEntity),
            new TableName(targetTable),
            deleteRuleCode: "Ignore",
            hasDatabaseConstraint: true,
            actualConstraints: new[] { constraint }).Value;
    }
}
