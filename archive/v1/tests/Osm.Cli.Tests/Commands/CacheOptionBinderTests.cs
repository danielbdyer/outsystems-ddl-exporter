using System.CommandLine;
using Osm.Cli.Commands.Binders;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class CacheOptionBinderTests
{
    [Fact]
    public void GetValue_ParsesCacheOptions()
    {
        var binder = new CacheOptionBinder();
        var command = new Command("test");
        foreach (var option in binder.Options)
        {
            command.AddOption(option);
        }

        var parseResult = command.Parse("--cache-root ./cache --refresh-cache");

        var overrides = binder.Bind(parseResult);

        Assert.Equal("./cache", overrides.Root);
        Assert.True(overrides.Refresh);
    }

    [Fact]
    public void GetValue_DefaultsWhenOptionsOmitted()
    {
        var binder = new CacheOptionBinder();
        var command = new Command("test");
        foreach (var option in binder.Options)
        {
            command.AddOption(option);
        }

        var parseResult = command.Parse(Array.Empty<string>());

        var overrides = binder.Bind(parseResult);

        Assert.Null(overrides.Root);
        Assert.Null(overrides.Refresh);
    }
}
