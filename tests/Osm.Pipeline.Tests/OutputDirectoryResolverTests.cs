using System;
using System.IO;
using Osm.Pipeline.Application;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class OutputDirectoryResolverTests
{
    [Fact]
    public void Resolve_DefaultsToOutWhenOverrideMissing()
    {
        using var temp = new TempDirectory();
        var original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = temp.Path;

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

            Assert.Equal(Path.Combine(temp.Path, "out"), result.OutputDirectory);
            Assert.Equal(Path.Combine(temp.Path, "out", "model.extracted.json"), result.ExtractedModelPath);
            Assert.True(Directory.Exists(result.OutputDirectory));
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void Resolve_ReturnsProvidedDirectory()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "custom");
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: target,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);
        var resolver = new OutputDirectoryResolver();

        var result = resolver.Resolve(overrides);

        Assert.Equal(target, result.OutputDirectory);
        Assert.Equal(Path.Combine(target, "model.extracted.json"), result.ExtractedModelPath);
        Assert.True(Directory.Exists(target));
    }
}
