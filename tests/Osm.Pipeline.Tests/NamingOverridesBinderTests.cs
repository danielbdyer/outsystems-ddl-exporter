using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class NamingOverridesBinderTests
{
    [Fact]
    public void Bind_ReturnsFailureForInvalidAssignment()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: "invalid",
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);
        var binder = new NamingOverridesBinder();

        var result = binder.Bind(overrides, TighteningOptions.Default);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, static error => error.Code == "cli.rename.invalidFormat");
    }

    [Fact]
    public void Bind_MergesOverridesWithConfiguration()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: "Module::Entity=CustomTable",
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);
        var binder = new NamingOverridesBinder();

        var result = binder.Bind(overrides, TighteningOptions.Default);

        Assert.True(result.IsSuccess);
        var hasOverride = result.Value.TryGetEntityOverride("Module", "Entity", out var tableName);
        Assert.True(hasOverride);
        Assert.Equal("CustomTable", tableName.Value);
    }
}
