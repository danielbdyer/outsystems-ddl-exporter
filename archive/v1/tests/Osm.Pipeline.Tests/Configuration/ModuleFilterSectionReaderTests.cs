using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Tests.Configuration;

public sealed class ModuleFilterSectionReaderTests
{
    [Fact]
    public void Read_WhenSectionMissing_ReturnsSentinel()
    {
        using var document = JsonDocument.Parse("{}");
        var reader = new ModuleFilterSectionReader();

        var result = reader.Read(document.RootElement, Directory.GetCurrentDirectory());

        result.Should().Be(ModuleFilterSectionReadResult.NotPresent);
    }

    [Fact]
    public void Read_WhenLegacyPathProvided_ResolvesAbsolutePath()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);
        var json = "{ \"model\": \"./fixtures/model.json\" }";
        using var document = JsonDocument.Parse(json);
        var reader = new ModuleFilterSectionReader();

        var result = reader.Read(document.RootElement, baseDirectory);

        result.HasValue.Should().BeTrue();
        result.ModelPath.Should().Be(Path.GetFullPath(Path.Combine(baseDirectory, "./fixtures/model.json")));
        result.ModuleFilter.Should().Be(ModuleFilterConfiguration.Empty);
    }

    [Fact]
    public void Read_WhenSectionPresent_ParsesModuleFilters()
    {
        const string Json = """
        {
            "model": {
                "path": "model.json",
                "modules": [
                    " Core ",
                    {
                        "name": "Analytics",
                        "entities": ["User", "user", "Order"],
                        "allowMissingPrimaryKey": ["Order"],
                        "allowMissingSchema": [true]
                    },
                    {
                        "name": "Support",
                        "entities": ["*"]
                    },
                    {
                        "name": "Governance",
                        "allowMissingPrimaryKey": ["Foo", "foo"],
                        "allowMissingSchema": ["Bar", "*"]
                    }
                ],
                "includeSystemModules": "true",
                "includeInactiveModules": false
            }
        }
        """;

        var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);
        using var document = JsonDocument.Parse(Json);
        var reader = new ModuleFilterSectionReader();

        var result = reader.Read(document.RootElement, baseDirectory);

        result.HasValue.Should().BeTrue();
        result.ModelPath.Should().Be(Path.GetFullPath(Path.Combine(baseDirectory, "model.json")));
        result.ModuleFilter.Modules.Should().Contain(new[] { "Core", "Analytics", "Support", "Governance" });
        result.ModuleFilter.IncludeSystemModules.Should().BeTrue();
        result.ModuleFilter.IncludeInactiveModules.Should().BeFalse();
        result.ModuleFilter.EntityFilters.Should().ContainKey("Analytics");
        result.ModuleFilter.EntityFilters["Analytics"].Should().BeEquivalentTo(new[] { "User", "Order" });
        result.ModuleFilter.EntityFilters.Should().NotContainKey("Support");
        result.ModuleFilter.ValidationOverrides.Should().ContainKey("Analytics");
        result.ModuleFilter.ValidationOverrides["Analytics"].AllowMissingPrimaryKey.Should().ContainSingle().Which.Should().Be("Order");
        result.ModuleFilter.ValidationOverrides.Should().ContainKey("Governance");
        var governanceOverrides = result.ModuleFilter.ValidationOverrides["Governance"];
        governanceOverrides.AllowMissingPrimaryKey.Should().BeEquivalentTo(new[] { "Foo" });
        governanceOverrides.AllowMissingPrimaryKeyForAll.Should().BeFalse();
        governanceOverrides.AllowMissingSchema.Should().BeEmpty();
        governanceOverrides.AllowMissingSchemaForAll.Should().BeTrue();
    }
}
