using System;
using System.IO;
using System.Threading.Tasks;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;

namespace Osm.Pipeline.Tests;

public class FixtureAdvancedSqlExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnFixtureJson()
    {
        var manifest = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));
        var executor = new FixtureAdvancedSqlExecutor(manifest);
        var request = new AdvancedSqlRequest(new[] { "AppCore", "ExtBilling", "Ops" }, includeSystemModules: false, onlyActiveAttributes: false);

        var result = await executor.ExecuteAsync(request);
        Assert.True(result.IsSuccess);
        Assert.Contains("\"modules\"", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnErrorWhenCaseMissing()
    {
        var manifest = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));
        var executor = new FixtureAdvancedSqlExecutor(manifest);
        var request = new AdvancedSqlRequest(new[] { "Unknown" }, includeSystemModules: false, onlyActiveAttributes: false);

        var result = await executor.ExecuteAsync(request);
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.fixture.missing", error.Code);
    }
}
