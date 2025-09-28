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
}
