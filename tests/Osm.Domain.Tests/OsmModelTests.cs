using System;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class OsmModelTests
{
    private static ModuleModel Module(string moduleName, string entityName)
    {
        var module = ModuleName.Create(moduleName).Value;
        var entity = Entity(module, entityName);
        return ModuleModel.Create(module, isSystemModule: false, isActive: true, new[] { entity }).Value;
    }

    private static EntityModel Entity(ModuleName module, string entityName)
    {
        var logical = EntityName.Create(entityName).Value;
        var table = TableName.Create($"OSUSR_{entityName.ToUpperInvariant()}").Value;
        var schema = SchemaName.Create("dbo").Value;
        var identifier = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None).Value;

        return EntityModel.Create(
            module,
            logical,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { identifier }).Value;
    }

    [Fact]
    public void Create_ShouldFail_WhenNoModulesProvided()
    {
        var exportedAt = DateTime.UtcNow;

        var result = OsmModel.Create(exportedAt, Array.Empty<ModuleModel>());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "model.modules.empty");
    }

    [Fact]
    public void Create_ShouldFail_WhenModuleNamesCollideIgnoringCase()
    {
        var exportedAt = DateTime.UtcNow;
        var first = Module("Sales", "Customer");
        var duplicate = Module("SALES", "Order");

        var result = OsmModel.Create(exportedAt, new[] { first, duplicate });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "model.modules.duplicate");
    }

    [Fact]
    public void Create_ShouldSucceed_WhenModulesAreDistinct()
    {
        var exportedAt = DateTime.UtcNow;
        var first = Module("Sales", "Customer");
        var second = Module("Inventory", "Stock");

        var result = OsmModel.Create(exportedAt, new[] { first, second });

        Assert.True(result.IsSuccess);
        Assert.Equal(exportedAt, result.Value.ExportedAtUtc);
        Assert.Equal(2, result.Value.Modules.Length);
        Assert.Contains(result.Value.Modules, m => m.Name.Value == "Sales");
        Assert.Contains(result.Value.Modules, m => m.Name.Value == "Inventory");
    }
}
