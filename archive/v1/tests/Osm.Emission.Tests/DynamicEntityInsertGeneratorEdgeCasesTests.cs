using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Emission.Tests;

public sealed class DynamicEntityInsertGeneratorEdgeCasesTests
{
    private static readonly SqlLiteralFormatter Formatter = new();

    [Fact]
    public async Task GenerateArtifacts_WithCyclicTableDependencies_DisablesConstraints()
    {
        // Setup A <-> B cycle
        var tableADef = CreateDefinition("App", "dbo", "TableA", "TableA", isIdentity: false);
        var tableBDef = CreateDefinition("App", "dbo", "TableB", "TableB", isIdentity: false);

        var rowA = StaticEntityRow.Create(new object?[] { 1, 1 }); // A.Id=1, A.B_Id=1
        var rowB = StaticEntityRow.Create(new object?[] { 1, 1 }); // B.Id=1, B.A_Id=1

        var dataset = new DynamicEntityDataset(ImmutableArray.Create(
            new StaticEntityTableData(tableADef, ImmutableArray.Create(rowA)),
            new StaticEntityTableData(tableBDef, ImmutableArray.Create(rowB))));

        // Define Model with Cyclic FKs
        var entityA = CreateEntityWithFK("TableA", "TableB", "B_Id", "Id", "FK_A_B");
        var entityB = CreateEntityWithFK("TableB", "TableA", "A_Id", "Id", "FK_B_A");

        var module = ModuleModel.Create(new ModuleName("App"), isSystemModule: false, isActive: true, entities: new[] { entityA, entityB }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var generator = new DynamicEntityInsertGenerator(Formatter);
        var artifacts = generator.GenerateArtifacts(dataset, ImmutableArray<StaticEntityTableData>.Empty, model: model);

        Assert.Equal(2, artifacts.Length);

        foreach (var artifact in artifacts)
        {
            var script = await GetScriptAsync(artifact);
            Assert.Contains("NOCHECK CONSTRAINT ALL", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CHECK CONSTRAINT ALL", script, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<string> GetScriptAsync(DynamicEntityInsertArtifact artifact)
    {
        using var writer = new StringWriter();
        await artifact.WriteAsync(writer, CancellationToken.None);
        return writer.ToString();
    }

    private static StaticEntitySeedTableDefinition CreateDefinition(
        string module,
        string schema,
        string physicalName,
        string logicalName,
        bool isIdentity)
    {
        var columns = ImmutableArray.Create(
            new StaticEntitySeedColumn("Id", "Id", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: isIdentity, IsNullable: false),
            new StaticEntitySeedColumn("FkId", "FkId", "FkId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: true));

        return new StaticEntitySeedTableDefinition(
            module,
            logicalName,
            schema,
            physicalName,
            logicalName,
            columns);
    }

    private static EntityModel CreateEntityWithFK(
        string tableName,
        string targetTableName,
        string fkColName,
        string targetColName,
        string constraintName)
    {
        var relationship = RelationshipModel.Create(
            new AttributeName(fkColName),
            new EntityName(targetTableName),
            new TableName(targetTableName),
            deleteRuleCode: "Ignore",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    constraintName,
                    referencedSchema: "dbo",
                    referencedTable: targetTableName,
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create(fkColName, fkColName, targetColName, targetColName, 0) })
            }).Value;

        return EntityModel.Create(
            new ModuleName("App"),
            new EntityName(tableName),
            new TableName(tableName),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute(fkColName, fkColName)
            },
            relationships: new[] { relationship }).Value;
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
