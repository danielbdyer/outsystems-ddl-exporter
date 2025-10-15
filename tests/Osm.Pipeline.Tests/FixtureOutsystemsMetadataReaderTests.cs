using System;
using System.Collections.Immutable;
using System.IO;
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
}
