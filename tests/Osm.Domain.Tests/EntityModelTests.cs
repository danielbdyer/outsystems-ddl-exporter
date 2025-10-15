using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public class EntityModelTests
{
    [Fact]
    public void Create_ShouldFail_WhenAttributesAreEmpty()
    {
        var module = ModuleName.Create("Module").Value;
        var logical = EntityName.Create("Entity").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_ENTITY").Value;

        var result = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            Array.Empty<AttributeModel>());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "entity.attributes.empty");
    }

    [Fact]
    public void Create_ShouldFail_WhenPrimaryKeyMissing()
    {
        var module = ModuleName.Create("Module").Value;
        var logical = EntityName.Create("Entity").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_ENTITY").Value;

        var attributeResult = AttributeModel.Create(
            AttributeName.Create("Name").Value,
            ColumnName.Create("NAME").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None);
        Assert.True(attributeResult.IsSuccess);
        var attribute = attributeResult.Value;

        var result = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { attribute });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "entity.attributes.missingPrimaryKey");
    }

    [Fact]
    public void Create_ShouldAllowMissingPrimaryKey_WhenOverrideEnabled()
    {
        var module = ModuleName.Create("Module").Value;
        var logical = EntityName.Create("Entity").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_ENTITY").Value;

        var attribute = AttributeModel.Create(
            AttributeName.Create("Name").Value,
            ColumnName.Create("NAME").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None).Value;

        var result = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { attribute },
            allowMissingPrimaryKey: true);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Create_ShouldFail_WhenDuplicateAttributeLogicalNamesDetected()
    {
        var module = ModuleName.Create("Module").Value;
        var logical = EntityName.Create("Entity").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_ENTITY").Value;

        var id = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None).Value;

        var duplicate = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID2").Value,
            dataType: "Identifier",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None).Value;

        var result = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { id, duplicate });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "entity.attributes.duplicateLogical");
    }

    [Fact]
    public void Create_ShouldFail_WhenDuplicateAttributePhysicalNamesDetectedIgnoringCase()
    {
        var module = ModuleName.Create("Module").Value;
        var logical = EntityName.Create("Entity").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_ENTITY").Value;

        var id = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None).Value;

        var conflicting = AttributeModel.Create(
            AttributeName.Create("Code").Value,
            ColumnName.Create("id").Value,
            dataType: "Text",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None).Value;

        var result = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { id, conflicting });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "entity.attributes.duplicateColumn");
    }

    [Fact]
    public void Create_ShouldSucceed_WhenInputsAreValid()
    {
        var module = ModuleName.Create("Module").Value;
        var logical = EntityName.Create("Entity").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_ENTITY").Value;

        var idResult = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None);
        Assert.True(idResult.IsSuccess);
        var id = idResult.Value;

        var nameResult = AttributeModel.Create(
            AttributeName.Create("Name").Value,
            ColumnName.Create("NAME").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None);
        Assert.True(nameResult.IsSuccess);
        var name = nameResult.Value;

        var indexResult = IndexModel.Create(
            IndexName.Create("PK_ENTITY").Value,
            isUnique: true,
            isPrimary: true,
            isPlatformAuto: false,
            new[]
            {
                IndexColumnModel.Create(
                    AttributeName.Create("Id").Value,
                    ColumnName.Create("ID").Value,
                    ordinal: 1,
                    isIncluded: false,
                    IndexColumnDirection.Ascending).Value
            });
        Assert.True(indexResult.IsSuccess);
        var index = indexResult.Value;

        var relationshipResult = RelationshipModel.Create(
            AttributeName.Create("Name").Value,
            EntityName.Create("Other").Value,
            TableName.Create("OSUSR_OTHER").Value,
            deleteRuleCode: "Protect",
            hasDatabaseConstraint: true);
        Assert.True(relationshipResult.IsSuccess);
        var relationship = relationshipResult.Value;

        var triggerResult = TriggerModel.Create(
            TriggerName.Create("TR_OSUSR_ENTITY_AUDIT").Value,
            isDisabled: false,
            "CREATE TRIGGER [dbo].[TR_OSUSR_ENTITY_AUDIT] ON [dbo].[OSUSR_ENTITY] AFTER INSERT AS BEGIN SET NOCOUNT ON; END");
        Assert.True(triggerResult.IsSuccess);
        var trigger = triggerResult.Value;

        var result = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { id, name },
            new[] { index },
            new[] { relationship },
            new[] { trigger });

        Assert.True(result.IsSuccess);
        Assert.Equal(module, result.Value.Module);
        Assert.Equal(2, result.Value.Attributes.Length);
        Assert.Single(result.Value.Relationships);
        Assert.Single(result.Value.Triggers);
    }

    [Fact]
    public void Create_ShouldFail_WhenDuplicateTriggerNames()
    {
        var module = ModuleName.Create("Module").Value;
        var logical = EntityName.Create("Entity").Value;
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("OSUSR_ENTITY").Value;

        var idResult = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None);
        Assert.True(idResult.IsSuccess);
        var id = idResult.Value;

        var trigger = TriggerModel.Create(
            TriggerName.Create("TR_OSUSR_ENTITY_AUDIT").Value,
            isDisabled: false,
            "CREATE TRIGGER [dbo].[TR_OSUSR_ENTITY_AUDIT] ON [dbo].[OSUSR_ENTITY] AFTER INSERT AS BEGIN RETURN; END").Value;
        var duplicate = trigger with { IsDisabled = true };

        var result = EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { id },
            triggers: new[] { trigger, duplicate });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "entity.triggers.duplicateName");
    }
}
