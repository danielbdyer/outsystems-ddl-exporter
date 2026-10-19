using System.Text.Json;
using Tests.Support;

namespace Osm.Json.Tests;

public sealed class FixtureSanityTests
{
    [Fact]
    public void EdgeCaseModel_Should_BeDiscoverable()
    {
        var path = FixtureFile.GetPath("model.edge-case.json");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void EdgeCaseModel_Should_ExposeThreeModules()
    {
        using var document = FixtureFile.OpenJson("model.edge-case.json");
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("modules", out var modules));
        Assert.Equal(JsonValueKind.Array, modules.ValueKind);
        Assert.Equal(3, modules.GetArrayLength());
    }
}
