using Osm.Pipeline.Application;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class OutputDirectoryResolverTests
{
    [Fact]
    public void Resolve_DefaultsToOutWhenOverrideMissing()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);
        var resolver = new OutputDirectoryResolver();

        var result = resolver.Resolve(overrides);

        Assert.Equal("out", result);
    }

    [Fact]
    public void Resolve_ReturnsProvidedDirectory()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: "custom",
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);
        var resolver = new OutputDirectoryResolver();

        var result = resolver.Resolve(overrides);

        Assert.Equal("custom", result);
    }
}
