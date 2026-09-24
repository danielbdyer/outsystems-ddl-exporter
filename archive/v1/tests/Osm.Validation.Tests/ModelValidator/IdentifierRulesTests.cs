using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Validation;

namespace Osm.Validation.Tests.ModelValidation;

public class IdentifierRulesTests
{
    private readonly ModelValidator _validator = new();

    [Fact]
    public void Validate_ShouldFail_WhenIdentifierMissing()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First();
        var withoutIdentifiers = entity.Attributes.Select(a => a with { IsIdentifier = false }).ToImmutableArray();
        var mutatedEntity = entity with { Attributes = withoutIdentifiers };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.identifier.missing");
    }

    [Fact]
    public void Validate_ShouldFail_WhenMultipleIdentifiersPresent()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First();
        var attributes = entity.Attributes
            .Select((attribute, index) => attribute with { IsIdentifier = index < 2 })
            .ToImmutableArray();
        var mutatedEntity = entity with { Attributes = attributes };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.identifier.multiple");
    }

    [Fact]
    public void Validate_ShouldFail_WhenIdentifierTypeNotIdentifier()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First();
        var attributes = entity.Attributes.Select(attribute => attribute.IsIdentifier
            ? attribute with { DataType = "Text" }
            : attribute).ToImmutableArray();
        var mutatedEntity = entity with { Attributes = attributes };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.identifier.typeMismatch");
    }
}
