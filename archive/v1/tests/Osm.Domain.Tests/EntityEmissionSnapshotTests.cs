using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Model.Emission;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public class EntityEmissionSnapshotTests
{
    [Fact]
    public void Create_FiltersInactiveAttributesAndSelectsIdentifiers()
    {
        var inactiveIdentifier = CreateAttribute(
            logicalName: "LegacyId",
            columnName: "LEGACYID",
            isIdentifier: true,
            presentButInactive: true);
        var activeIdentifier = CreateAttribute(
            logicalName: "CustomerId",
            columnName: "CUSTOMERID",
            isIdentifier: true);
        var inactiveAttribute = CreateAttribute(
            logicalName: "IsDeleted",
            columnName: "ISDELETED",
            isIdentifier: false,
            isActive: false);
        var regularAttribute = CreateAttribute("Name", "NAME");

        var entity = CreateEntity(
            moduleName: "Sales",
            logicalName: "Customer",
            physicalName: "OSUSR_SALES_CUSTOMER",
            schema: "sales",
            inactiveIdentifier,
            activeIdentifier,
            inactiveAttribute,
            regularAttribute);

        var snapshot = EntityEmissionSnapshot.Create("Sales", entity);

        Assert.Equal(new[] { "CustomerId", "Name" }, snapshot.EmittableAttributes.Select(a => a.LogicalName.Value));
        Assert.Single(snapshot.IdentifierAttributes);
        Assert.Equal("CustomerId", snapshot.IdentifierAttributes[0].LogicalName.Value);
        Assert.True(snapshot.AttributeLookup.ContainsKey("CUSTOMERID"));
        Assert.False(snapshot.AttributeLookup.ContainsKey("LEGACYID"));
        Assert.Same(activeIdentifier, snapshot.ActiveIdentifier);
        Assert.Same(inactiveIdentifier, snapshot.FallbackIdentifier);
        Assert.Same(activeIdentifier, snapshot.PreferredIdentifier);
    }

    private static EntityModel CreateEntity(
        string moduleName,
        string logicalName,
        string physicalName,
        string schema,
        params AttributeModel[] attributes)
    {
        var result = EntityModel.Create(
            new ModuleName(moduleName),
            new EntityName(logicalName),
            new TableName(physicalName),
            new SchemaName(schema),
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: attributes);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AttributeModel CreateAttribute(
        string logicalName,
        string columnName,
        bool isIdentifier = false,
        bool isActive = true,
        bool presentButInactive = false)
    {
        var reality = new AttributeReality(null, null, null, null, presentButInactive);
        var result = AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: isActive,
            reality: reality,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
