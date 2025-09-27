using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class ModuleModelTests
{
    private static EntityModel Entity(string logical, string physical)
    {
        var module = ModuleName.Create("Module").Value;
        var entityName = EntityName.Create(logical).Value;
        var table = TableName.Create(physical).Value;
        var schema = SchemaName.Create("dbo").Value;

        var id = AttributeModel.Create(
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
            entityName,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { id }).Value;
    }

    [Fact]
    public void Create_ShouldFail_WhenEntitiesMissing()
    {
        var module = ModuleName.Create("Module").Value;

        var result = ModuleModel.Create(module, isSystemModule: false, isActive: true, Array.Empty<EntityModel>());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "module.entities.empty");
    }

    [Fact]
    public void Create_ShouldFail_WhenDuplicateLogicalNames()
    {
        var module = ModuleName.Create("Module").Value;
        var entity = Entity("Customer", "OSUSR_CUST");

        var result = ModuleModel.Create(module, false, true, new[] { entity, entity });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "module.entities.duplicateLogical");
    }

    [Fact]
    public void Create_ShouldFail_WhenDuplicatePhysicalNamesIgnoringCase()
    {
        var module = ModuleName.Create("Module").Value;
        var first = Entity("Customer", "OSUSR_CUST");
        var second = Entity("CustomerArchive", "osusr_cust");

        var result = ModuleModel.Create(module, false, true, new[] { first, second });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "module.entities.duplicatePhysical");
    }

    [Fact]
    public void Create_ShouldSucceed_WhenEntitiesAreUnique()
    {
        var module = ModuleName.Create("Module").Value;
        var first = Entity("Customer", "OSUSR_CUST");
        var second = Entity("Order", "OSUSR_ORDR");

        var result = ModuleModel.Create(module, false, true, new[] { first, second });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entities.Length);
    }
}
