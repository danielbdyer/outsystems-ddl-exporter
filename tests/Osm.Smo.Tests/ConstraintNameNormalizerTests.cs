using System;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class ConstraintNameNormalizerTests
{
    [Fact]
    public void Normalize_ForeignKey_ReplacesTableSuffixWhenFullPhysicalNameNotInConstraintName()
    {
        // Arrange: Simulate scenario where FK name contains only the suffix of the physical table name
        // Child table: Project (physical: OSUSR_wzu_Project)
        // Parent table: State (physical: OSUSR_wzu_ProjectStatus, logical: State)
        // FK name from DB: OSFRK_OSUSR_WZU_PROJECT_OSUSR_WZU_PROJECTSTATUS_STATEID
        // After replacing child table name: OSFRK_Project_OSUSR_WZU_PROJECTSTATUS_STATEID
        // The full physical name "OSUSR_wzu_ProjectStatus" is not in the constraint name
        // But the suffix "ProjectStatus" (after removing prefix) is present as "PROJECTSTATUS"

        var childEntity = CreateEntity(
            logicalName: "Project",
            physicalName: "OSUSR_wzu_Project");

        var parentEntity = CreateEntity(
            logicalName: "State",
            physicalName: "OSUSR_wzu_ProjectStatus");

        var stateIdAttribute = CreateAttribute("StateId", "StateId");

        var originalConstraintName = "OSFRK_OSUSR_WZU_PROJECT_OSUSR_WZU_PROJECTSTATUS_STATEID";

        // Act
        var normalized = ConstraintNameNormalizer.Normalize(
            originalConstraintName,
            childEntity,
            new[] { stateIdAttribute },
            ConstraintNameKind.ForeignKey,
            SmoFormatOptions.Default,
            referencedEntity: parentEntity);

        // Assert: Should produce OSFRK_Project_State_StateId, not OSFRK_Project_ProjectStatus_StateId
        Assert.Equal("OSFRK_Project_State_StateId", normalized);
    }

    [Fact]
    public void Normalize_ForeignKey_WorksWithStandardNaming()
    {
        // Arrange: Standard case where full table names work fine
        var childEntity = CreateEntity(
            logicalName: "Customer",
            physicalName: "OSUSR_abc_Customer");

        var parentEntity = CreateEntity(
            logicalName: "City",
            physicalName: "OSUSR_abc_City");

        var cityIdAttribute = CreateAttribute("CityId", "CityId");

        var originalConstraintName = "OSFRK_OSUSR_ABC_CUSTOMER_OSUSR_ABC_CITY_CITYID";

        // Act
        var normalized = ConstraintNameNormalizer.Normalize(
            originalConstraintName,
            childEntity,
            new[] { cityIdAttribute },
            ConstraintNameKind.ForeignKey,
            SmoFormatOptions.Default,
            referencedEntity: parentEntity);

        // Assert
        Assert.Equal("OSFRK_Customer_City_CityId", normalized);
    }

    [Fact]
    public void Normalize_ForeignKey_HandlesTablesWithSingleUnderscore()
    {
        // Arrange: Table name with only one underscore (PREFIX_TableName)
        var childEntity = CreateEntity(
            logicalName: "Order",
            physicalName: "PREFIX_Order");

        var parentEntity = CreateEntity(
            logicalName: "Product",
            physicalName: "PREFIX_Product");

        var productIdAttribute = CreateAttribute("ProductId", "ProductId");

        var originalConstraintName = "FK_PREFIX_ORDER_PREFIX_PRODUCT_PRODUCTID";

        // Act
        var normalized = ConstraintNameNormalizer.Normalize(
            originalConstraintName,
            childEntity,
            new[] { productIdAttribute },
            ConstraintNameKind.ForeignKey,
            SmoFormatOptions.Default,
            referencedEntity: parentEntity);

        // Assert
        Assert.Equal("FK_Order_Product_ProductId", normalized);
    }

    private static EntityModel CreateEntity(string logicalName, string physicalName)
    {
        var result = EntityModel.Create(
            module: ModuleName.Create("TestModule").Value,
            logicalName: EntityName.Create(logicalName).Value,
            physicalName: TableName.Create(physicalName).Value,
            schema: SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "Id", isIdentifier: true)
            },
            indexes: Array.Empty<IndexModel>(),
            relationships: Array.Empty<RelationshipModel>(),
            triggers: Array.Empty<TriggerModel>(),
            metadata: EntityMetadata.Empty,
            allowMissingPrimaryKey: false);

        return result.Value;
    }

    private static AttributeModel CreateAttribute(string logicalName, string columnName, bool isIdentifier = false)
    {
        var result = AttributeModel.Create(
            logicalName: AttributeName.Create(logicalName).Value,
            columnName: ColumnName.Create(columnName).Value,
            dataType: "Text",
            isMandatory: false,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: true,
            length: 50);

        return result.Value;
    }
}
