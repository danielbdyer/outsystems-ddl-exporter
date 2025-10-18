using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;

namespace Osm.Pipeline.Tests;

public class FixtureOutsystemsMetadataReaderTests
{
    [Fact]
    public async Task ReadAsync_ShouldReturnFixtureSnapshot()
    {
        var manifest = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));
        var reader = new FixtureOutsystemsMetadataReader(manifest);
        var request = new AdvancedSqlRequest(
            ImmutableArray.Create(
                ModuleName.Create("AppCore").Value,
                ModuleName.Create("ExtBilling").Value,
                ModuleName.Create("Ops").Value),
            includeSystemModules: false,
            onlyActiveAttributes: false);

        var result = await reader.ReadAsync(request);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.ModuleJson);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnErrorWhenCaseMissing()
    {
        var manifest = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));
        var reader = new FixtureOutsystemsMetadataReader(manifest);
        var request = new AdvancedSqlRequest(
            ImmutableArray.Create(ModuleName.Create("Unknown").Value),
            includeSystemModules: false,
            onlyActiveAttributes: false);

        var result = await reader.ReadAsync(request);
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.fixture.missing", error.Code);
    }

    [Fact]
    public async Task ReadAsync_ShouldEmitEmptyCollections_WhenOnlyActiveAttributesCaseMatches()
    {
        var manifest = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));
        var reader = new FixtureOutsystemsMetadataReader(manifest);
        var request = new AdvancedSqlRequest(
            ImmutableArray.Create(ModuleName.Create("AppCore").Value),
            includeSystemModules: false,
            onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request);

        Assert.True(result.IsSuccess);
        var module = Assert.Single(result.Value.ModuleJson);
        using var document = JsonDocument.Parse(module.ModuleEntitiesJson);
        var entities = document.RootElement;
        Assert.Equal(JsonValueKind.Array, entities.ValueKind);
        var entity = Assert.Single(entities.EnumerateArray());

        Assert.True(entity.TryGetProperty("attributes", out var attributes));
        Assert.Equal(JsonValueKind.Array, attributes.ValueKind);
        Assert.Empty(attributes.EnumerateArray());

        Assert.True(entity.TryGetProperty("relationships", out var relationships));
        Assert.Equal(JsonValueKind.Array, relationships.ValueKind);
        Assert.Empty(relationships.EnumerateArray());

        Assert.True(entity.TryGetProperty("indexes", out var indexes));
        Assert.Equal(JsonValueKind.Array, indexes.ValueKind);
        Assert.Empty(indexes.EnumerateArray());

        Assert.True(entity.TryGetProperty("triggers", out var triggers));
        Assert.Equal(JsonValueKind.Array, triggers.ValueKind);
        Assert.Empty(triggers.EnumerateArray());
    }
}
