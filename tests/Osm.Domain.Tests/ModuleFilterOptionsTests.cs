using System.Linq;
using Osm.Domain.Configuration;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class ModuleFilterOptionsTests
{
    [Fact]
    public void Create_ReturnsIncludeAll_WhenModulesNull()
    {
        var result = ModuleFilterOptions.Create(null, includeSystemModules: true, includeInactiveModules: true);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Modules.IsEmpty);
        Assert.True(result.Value.IncludeSystemModules);
        Assert.True(result.Value.IncludeInactiveModules);
    }

    [Fact]
    public void Create_RejectsNullModuleName()
    {
        var modules = new string?[] { "AppCore", null };
        var result = ModuleFilterOptions.Create(modules.Select(value => value!), true, true);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "moduleFilter.modules.null");
    }

    [Fact]
    public void Create_SortsAndDeduplicatesModules()
    {
        var result = ModuleFilterOptions.Create(new[] { "Ops", "AppCore", "ops" }, includeSystemModules: false, includeInactiveModules: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "AppCore", "Ops" }, result.Value.Modules);
        Assert.False(result.Value.IncludeSystemModules);
        Assert.True(result.Value.IncludeInactiveModules);
    }

    [Fact]
    public void Merge_AddsNewModules()
    {
        var options = ModuleFilterOptions.Create(new[] { "AppCore" }, true, true).Value;

        var merged = options.Merge(new[] { "Ops", "AppCore", "ExtBilling" });

        Assert.Equal(new[] { "AppCore", "ExtBilling", "Ops" }, merged.Modules);
        Assert.True(merged.IncludeSystemModules);
        Assert.True(merged.IncludeInactiveModules);
    }
}
