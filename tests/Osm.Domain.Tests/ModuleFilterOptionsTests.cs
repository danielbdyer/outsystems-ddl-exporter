using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
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
        Assert.True(result.Value.EntityFilters.IsEmpty);
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
    public void Create_RejectsWhitespaceModuleName()
    {
        var result = ModuleFilterOptions.Create(new[] { "AppCore", "   " }, includeSystemModules: true, includeInactiveModules: true);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "moduleFilter.modules.empty");
    }

    [Fact]
    public void Create_SortsAndDeduplicatesModules()
    {
        var result = ModuleFilterOptions.Create(new[] { "Ops", "AppCore", "ops" }, includeSystemModules: false, includeInactiveModules: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "AppCore", "Ops" }, result.Value.Modules.Select(static module => module.Value));
        Assert.False(result.Value.IncludeSystemModules);
        Assert.True(result.Value.IncludeInactiveModules);
        Assert.True(result.Value.EntityFilters.IsEmpty);
    }

    [Fact]
    public void Create_Allows256CharacterModuleName()
    {
        var module = new string('A', 256);

        var result = ModuleFilterOptions.Create(new[] { module }, includeSystemModules: true, includeInactiveModules: true);

        Assert.True(result.IsSuccess);
        var moduleName = Assert.Single(result.Value.Modules);
        Assert.Equal(module, moduleName.Value);
    }

    [Fact]
    public void Merge_AddsNewModules()
    {
        var options = ModuleFilterOptions.Create(new[] { "AppCore" }, true, true).Value;

        var merged = options.Merge(new[]
        {
            ModuleName.Create("Ops").Value,
            ModuleName.Create("AppCore").Value,
            ModuleName.Create("ExtBilling").Value
        });

        Assert.Equal(new[] { "AppCore", "ExtBilling", "Ops" }, merged.Modules.Select(static module => module.Value));
        Assert.True(merged.IncludeSystemModules);
        Assert.True(merged.IncludeInactiveModules);
        Assert.True(merged.EntityFilters.IsEmpty);
    }

    [Fact]
    public void Merge_IgnoresDuplicateModules()
    {
        var options = ModuleFilterOptions.Create(new[] { "AppCore" }, includeSystemModules: false, includeInactiveModules: false).Value;

        var merged = options.Merge(new[]
        {
            ModuleName.Create("AppCore").Value,
            ModuleName.Create("Ops").Value,
            ModuleName.Create("appcore").Value
        });

        Assert.Equal(new[] { "AppCore", "Ops" }, merged.Modules.Select(static module => module.Value));
        Assert.False(merged.IncludeSystemModules);
        Assert.False(merged.IncludeInactiveModules);
        Assert.True(merged.EntityFilters.IsEmpty);
    }

    [Fact]
    public void WithIncludeSystemModules_ShouldUpdateFlag()
    {
        var options = ModuleFilterOptions.Create(new[] { "AppCore" }, includeSystemModules: false, includeInactiveModules: true).Value;

        var updated = options.WithIncludeSystemModules(include: true);

        Assert.Equal(options.Modules, updated.Modules);
        Assert.True(updated.IncludeSystemModules);
        Assert.True(updated.IncludeInactiveModules);
        Assert.Equal(options.EntityFilters, updated.EntityFilters);
    }

    [Fact]
    public void WithIncludeInactiveModules_ShouldUpdateFlag()
    {
        var options = ModuleFilterOptions.Create(new[] { "AppCore" }, includeSystemModules: true, includeInactiveModules: false).Value;

        var updated = options.WithIncludeInactiveModules(include: true);

        Assert.Equal(options.Modules, updated.Modules);
        Assert.True(updated.IncludeSystemModules);
        Assert.True(updated.IncludeInactiveModules);
        Assert.Equal(options.EntityFilters, updated.EntityFilters);
    }

    [Fact]
    public void Create_ParsesEntityFilters()
    {
        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ServiceCenter"] = new[] { "User", "OSUSR_U_USER" }
        };

        var result = ModuleFilterOptions.Create(new[] { "ServiceCenter" }, true, true, filters);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.EntityFilters);
        var filter = result.Value.EntityFilters["ServiceCenter"];
        Assert.Equal(new[] { "User", "OSUSR_U_USER" }, filter.Names);
    }

    [Fact]
    public void Create_ReturnsFailure_WhenEntityFilterEmpty()
    {
        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppCore"] = Array.Empty<string>()
        };

        var result = ModuleFilterOptions.Create(new[] { "AppCore" }, true, true, filters);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "moduleFilter.entities.empty");
    }
}
