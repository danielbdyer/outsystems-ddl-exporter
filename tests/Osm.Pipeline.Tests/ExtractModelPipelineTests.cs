using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Json;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Sql;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class ExtractModelPipelineTests
{
    [Fact]
    public async Task HandleAsync_ShouldUseFixtureExecutorWhenManifestProvided()
    {
        var pipeline = CreatePipeline();
        var command = ModelExtractionCommand.Create(new[] { "AppCore", "ExtBilling", "Ops" }, includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: false).Value;
        var sqlOptions = new ResolvedSqlOptions(
            ConnectionString: null,
            CommandTimeoutSeconds: null,
            Sampling: new SqlSamplingSettings(null, null),
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            MetadataContract: MetadataContractOverrides.Strict);
        var manifestPath = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));
        var request = new ExtractModelPipelineRequest(command, sqlOptions, manifestPath, OutputPath: null, SqlMetadataOutputPath: null, SqlMetadataLog: null);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors.Select(error => $"{error.Code}:{error.Message}")));
        Assert.NotNull(result.Value.JsonPayload);
        Assert.NotNull(result.Value.Model);
    }

    [Fact]
    public async Task HandleAsync_ShouldFailWhenConnectionStringMissingForLiveExtraction()
    {
        var pipeline = CreatePipeline();
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: false).Value;
        var sqlOptions = new ResolvedSqlOptions(
            ConnectionString: null,
            CommandTimeoutSeconds: 30,
            Sampling: new SqlSamplingSettings(null, null),
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            MetadataContract: MetadataContractOverrides.Strict);
        var request = new ExtractModelPipelineRequest(command, sqlOptions, AdvancedSqlFixtureManifestPath: null, OutputPath: null, SqlMetadataOutputPath: null, SqlMetadataLog: null);

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.extractModel.sqlConnection.missing", error.Code);
    }
    private static ExtractModelPipeline CreatePipeline()
    {
        return new ExtractModelPipeline(new ModelJsonDeserializer(), NullLoggerFactory.Instance);
    }
}
