using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Validation;

namespace Osm.Validation.Tests.ModelValidation;

public class AttributeUniquenessTests
{
    private readonly ModelValidator _validator = new();

    [Fact]
    public void Validate_ShouldFlagDuplicateLogicalAttributeNames()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First();
        var duplicateSource = entity.Attributes.First();
        var duplicate = duplicateSource with
        {
            ColumnName = ColumnName.Create("EMAIL_DUP").Value,
            IsIdentifier = false
        };

        var mutatedEntity = entity with { Attributes = entity.Attributes.Add(duplicate) };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.attributes.duplicateLogical");
    }

    [Fact]
    public void Validate_ShouldFlagDuplicatePhysicalAttributeNames()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First();
        var duplicateSource = entity.Attributes.First();
        var duplicate = duplicateSource with
        {
            LogicalName = AttributeName.Create("DuplicateLogical").Value,
            IsIdentifier = false
        };

        var mutatedEntity = entity with { Attributes = entity.Attributes.Add(duplicate) };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.attributes.duplicatePhysical");
    }
}
