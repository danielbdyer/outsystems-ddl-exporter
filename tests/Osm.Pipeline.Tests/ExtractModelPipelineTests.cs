using System;
using System.Threading.Tasks;
using System.Linq;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class ExtractModelPipelineTests
{
    [Fact]
    public async Task HandleAsync_ShouldUseFixtureExecutorWhenManifestProvided()
    {
        var pipeline = new ExtractModelPipeline();
        var command = ModelExtractionCommand.Create(new[] { "AppCore", "ExtBilling", "Ops" }, includeSystemModules: false, onlyActiveAttributes: false).Value;
        var sqlOptions = new ResolvedSqlOptions(
            ConnectionString: null,
            CommandTimeoutSeconds: null,
            Sampling: new SqlSamplingSettings(null, null),
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            MetadataContract: MetadataContractOverrides.Strict);
        var manifestPath = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));
        var request = new ExtractModelPipelineRequest(command, sqlOptions, manifestPath, SqlMetadataOutputPath: null);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors.Select(error => $"{error.Code}:{error.Message}")));
        Assert.NotNull(result.Value.JsonPayload);
        Assert.NotNull(result.Value.Model);
    }

    [Fact]
    public async Task HandleAsync_ShouldFailWhenConnectionStringMissingForLiveExtraction()
    {
        var pipeline = new ExtractModelPipeline();
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;
        var sqlOptions = new ResolvedSqlOptions(
            ConnectionString: null,
            CommandTimeoutSeconds: 30,
            Sampling: new SqlSamplingSettings(null, null),
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            MetadataContract: MetadataContractOverrides.Strict);
        var request = new ExtractModelPipelineRequest(command, sqlOptions, AdvancedSqlFixtureManifestPath: null, SqlMetadataOutputPath: null);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.extractModel.sqlConnection.missing", error.Code);
    }
}
