using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Pipeline.ModelIngestion;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class ModuleFilterTests
{
    [Fact]
    public void Apply_ReturnsModel_WhenFilterEmpty()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var filter = ModuleFilterOptions.IncludeAll;

        var result = new ModuleFilter().Apply(model, filter);

        Assert.True(result.IsSuccess);
        Assert.Equal(model, result.Value);
    }

    [Fact]
    public void Apply_FiltersToSpecifiedModules()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var filterOptions = ModuleFilterOptions.Create(new[] { "AppCore", "Ops" }, includeSystemModules: true, includeInactiveModules: true).Value;

        var result = new ModuleFilter().Apply(model, filterOptions);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "AppCore", "Ops" }, result.Value.Modules.Select(m => m.Name.Value));
    }

    [Fact]
    public void Apply_ReturnsFailure_WhenModuleMissing()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var filterOptions = ModuleFilterOptions.Create(new[] { "Missing" }, true, true).Value;

        var result = new ModuleFilter().Apply(model, filterOptions);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "modelFilter.modules.missing");
    }

    [Fact]
    public void Apply_ExcludesSystemAndInactiveModules()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var systemModule = model.Modules[0] with { IsSystemModule = true };
        var inactiveModule = model.Modules[1] with { IsActive = false };
        var activeModule = model.Modules[2];

        var adjusted = Osm.Domain.Model.OsmModel.Create(
            model.ExportedAtUtc,
            new[] { systemModule, inactiveModule, activeModule }).Value;

        var filterOptions = ModuleFilterOptions.Create(null, includeSystemModules: false, includeInactiveModules: false).Value;

        var result = new ModuleFilter().Apply(adjusted, filterOptions);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Modules, module =>
        {
            Assert.False(module.IsSystemModule);
            Assert.True(module.IsActive);
        });
        Assert.Equal(new[] { activeModule.Name.Value }, result.Value.Modules.Select(m => m.Name.Value));
    }

    [Fact]
    public void Apply_RemovesInactiveEntities_WhenInactiveModulesExcluded()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var sourceModule = model.Modules[0];
        Assert.True(sourceModule.Entities.Length > 1);

        var entitiesBuilder = sourceModule.Entities.ToBuilder();
        entitiesBuilder[0] = entitiesBuilder[0] with { IsActive = false };
        var mutatedModule = sourceModule with { Entities = entitiesBuilder.ToImmutable() };

        var modulesBuilder = model.Modules.ToBuilder();
        modulesBuilder[0] = mutatedModule;
        var mutatedModel = model with { Modules = modulesBuilder.ToImmutable() };

        var includeInactive = ModuleFilterOptions.Create(
            new[] { mutatedModule.Name.Value },
            includeSystemModules: true,
            includeInactiveModules: true).Value;

        var includeInactiveResult = new ModuleFilter().Apply(mutatedModel, includeInactive);

        Assert.True(includeInactiveResult.IsSuccess);
        var moduleWithInactive = Assert.Single(includeInactiveResult.Value.Modules);
        Assert.Contains(moduleWithInactive.Entities, entity => !entity.IsActive);

        var excludeInactive = ModuleFilterOptions.Create(
            new[] { mutatedModule.Name.Value },
            includeSystemModules: true,
            includeInactiveModules: false).Value;

        var excludeInactiveResult = new ModuleFilter().Apply(mutatedModel, excludeInactive);

        Assert.True(excludeInactiveResult.IsSuccess);
        var filteredModule = Assert.Single(excludeInactiveResult.Value.Modules);
        Assert.All(filteredModule.Entities, entity => Assert.True(entity.IsActive));
        Assert.True(filteredModule.Entities.Length < moduleWithInactive.Entities.Length);
    }

    [Fact]
    public void Apply_FiltersEntitiesWithinModule()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppCore"] = new[] { "Customer" }
        };

        var filterOptions = ModuleFilterOptions.Create(new[] { "AppCore" }, true, true, filters).Value;

        var result = new ModuleFilter().Apply(model, filterOptions);

        Assert.True(result.IsSuccess);
        var module = Assert.Single(result.Value.Modules);
        Assert.Equal("AppCore", module.Name.Value);
        var entity = Assert.Single(module.Entities);
        Assert.Equal("Customer", entity.LogicalName.Value);
    }

    [Fact]
    public void Apply_ReturnsFailure_WhenEntityMissing()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppCore"] = new[] { "MissingEntity" }
        };

        var filterOptions = ModuleFilterOptions.Create(new[] { "AppCore" }, true, true, filters).Value;

        var result = new ModuleFilter().Apply(model, filterOptions);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "modelFilter.entities.missing");
    }
}
