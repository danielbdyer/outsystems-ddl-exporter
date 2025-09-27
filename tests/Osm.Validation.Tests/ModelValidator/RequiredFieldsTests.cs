using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Validation;

namespace Osm.Validation.Tests.ModelValidation;

public class RequiredFieldsTests
{
    private readonly ModelValidator _validator = new();

    [Fact]
    public void Validate_ShouldFail_WhenModelContainsNoModules()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var mutated = model with { Modules = ImmutableArray<ModuleModel>.Empty };

        var report = _validator.Validate(mutated);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "model.modules.empty" && m.Path == "modules");
    }

    [Fact]
    public void Validate_ShouldFail_WhenModuleContainsNoEntities()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules[0];
        var mutatedModule = module with { Entities = ImmutableArray<EntityModel>.Empty };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "module.entities.empty" && m.Path == $"modules[{mutatedModule.Name.Value}]");
    }
}
