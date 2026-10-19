using System.CommandLine;
using Osm.Cli.Commands.Binders;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class ModuleFilterOptionBinderTests
{
    [Fact]
    public void GetValue_ParsesModuleFilters()
    {
        var binder = new ModuleFilterOptionBinder();
        var command = new Command("test");
        foreach (var option in binder.Options)
        {
            command.AddOption(option);
        }

        var parseResult = command.Parse("--modules ModuleA;ModuleB --include-system-modules --include-inactive-modules --allow-missing-primary-key Module1::Entity Module2::* --allow-missing-schema Module3::Entity");

        var overrides = binder.Bind(parseResult);

        Assert.Equal(new[] { "ModuleA", "ModuleB" }, overrides.Modules);
        Assert.True(overrides.IncludeSystemModules);
        Assert.True(overrides.IncludeInactiveModules);
        Assert.Equal(new[] { "Module1::Entity", "Module2::*" }, overrides.AllowMissingPrimaryKey);
        Assert.Equal(new[] { "Module3::Entity" }, overrides.AllowMissingSchema);
    }

    [Fact]
    public void GetValue_ResolvesExcludeFlags()
    {
        var binder = new ModuleFilterOptionBinder();
        var command = new Command("test");
        foreach (var option in binder.Options)
        {
            command.AddOption(option);
        }

        var parseResult = command.Parse("--exclude-system-modules --only-active-modules");

        var overrides = binder.Bind(parseResult);

        Assert.False(overrides.IncludeSystemModules);
        Assert.False(overrides.IncludeInactiveModules);
    }
}
