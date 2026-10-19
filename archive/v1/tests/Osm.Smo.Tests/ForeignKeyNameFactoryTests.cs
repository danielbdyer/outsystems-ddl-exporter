using System;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Smo.Tests;

public class ForeignKeyNameFactoryTests
{
    [Fact]
    public void CreateEvidenceName_RebuildsLogicalNameWhenEvidenceExceedsLimit()
    {
        var ownerContext = CreateContext(
            module: "Finance",
            logicalName: "PortfolioLedger_PositionDetail",
            physicalName: "OSUSR_FIN_PortfolioLedger_PositionDetail",
            attributeLogicalName: "PortfolioLedgerId",
            attributeColumnName: "PortfolioLedgerId");

        var referencedContext = CreateContext(
            module: "Finance",
            logicalName: "PortfolioLedger",
            physicalName: "OSUSR_FIN_PortfolioLedger",
            attributeLogicalName: "Id",
            attributeColumnName: "Id");

        var ownerAttribute = ownerContext.Entity.Attributes[0];
        var ownerColumns = ImmutableArray.Create(ownerAttribute.ColumnName.Value);
        var ownerAttributes = ImmutableArray.Create(ownerAttribute);
        var oversizedName = "OSFRK_" + new string('A', 200);

        var result = ForeignKeyNameFactory.CreateEvidenceName(
            ownerContext,
            referencedContext,
            oversizedName,
            ownerColumns,
            ownerAttributes,
            referencedContext.Entity.PhysicalName.Value,
            SmoFormatOptions.Default);

        Assert.Equal(
            "FK_PortfolioLedger_PositionDetail_PortfolioLedger_PortfolioLedgerId",
            result);
    }

    [Fact]
    public void CreateEvidenceName_TruncatesRebuiltNameWithHashWhenStillTooLong()
    {
        var ownerContext = CreateContext(
            module: "Ops",
            logicalName: "SuperLongOwnerEntity_ForOperationalHistory_WithExtremelyVerboseDescriptors",
            physicalName: "OSUSR_OPS_SuperLongOwnerEntity_ForOperationalHistory_WithExtremelyVerboseDescriptors",
            attributeLogicalName: "OperationalHistoryCorrelationIdentifier",
            attributeColumnName: "OperationalHistoryCorrelationIdentifier");

        var referencedContext = CreateContext(
            module: "Ops",
            logicalName: "AnotherEntityName_WithVerboseDescriptors_ForLinking",
            physicalName: "OSUSR_OPS_AnotherEntityName_WithVerboseDescriptors_ForLinking",
            attributeLogicalName: "Id",
            attributeColumnName: "Id");

        var ownerAttribute = ownerContext.Entity.Attributes[0];
        var ownerColumns = ImmutableArray.Create(ownerAttribute.ColumnName.Value);
        var ownerAttributes = ImmutableArray.Create(ownerAttribute);
        var oversizedName = "OSFRK_" + new string('Z', 256);

        var result = ForeignKeyNameFactory.CreateEvidenceName(
            ownerContext,
            referencedContext,
            oversizedName,
            ownerColumns,
            ownerAttributes,
            referencedContext.Entity.PhysicalName.Value,
            SmoFormatOptions.Default);

        var logicalBase = "FK_" +
            "SuperLongOwnerEntity_ForOperationalHistory_WithExtremelyVerboseDescriptors_" +
            "AnotherEntityName_WithVerboseDescriptors_ForLinking_" +
            "OperationalHistoryCorrelationIdentifier";
        var expected = TruncateWithHash(logicalBase);

        Assert.Equal(128, result.Length);
        Assert.Equal(expected, result);
    }

    private static EntityEmissionContext CreateContext(
        string module,
        string logicalName,
        string physicalName,
        string attributeLogicalName,
        string attributeColumnName)
    {
        var attribute = AttributeModel.Create(
            AttributeName.Create(attributeLogicalName).Value,
            ColumnName.Create(attributeColumnName).Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true,
            length: 50).Value;

        var entity = EntityModel.Create(
            ModuleName.Create(module).Value,
            EntityName.Create(logicalName).Value,
            TableName.Create(physicalName).Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { attribute },
            indexes: System.Array.Empty<IndexModel>(),
            relationships: System.Array.Empty<RelationshipModel>(),
            triggers: System.Array.Empty<TriggerModel>(),
            metadata: EntityMetadata.Empty,
            allowMissingPrimaryKey: false).Value;

        return EntityEmissionContext.Create(module, entity);
    }

    private static string TruncateWithHash(string value)
    {
        const int maxLength = 128;
        const int hashLength = 12;
        if (value.Length <= maxLength)
        {
            return value;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var usableHash = hash[..hashLength];
        var available = maxLength - hashLength - 1;
        var prefix = value[..available].TrimEnd('_');
        if (prefix.Length == 0)
        {
            prefix = value[..available];
        }

        return $"{prefix}_{usableHash}";
    }
}
