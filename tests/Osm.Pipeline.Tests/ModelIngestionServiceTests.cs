using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using Osm.Json;
using Osm.Pipeline.ModelIngestion;
using Xunit;

namespace Osm.Pipeline.Tests;

public class ModelIngestionServiceTests
{
    private static MockFileSystem CreateFileSystem(string path, string content)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(path, new MockFileData(content, Encoding.UTF8));
        return fileSystem;
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldReturnModel_WhenFileExists()
    {
        const string path = @"/data/model.json";
        const string json = "{\n" +
            "  \"exportedAtUtc\": \"2025-01-01T00:00:00Z\",\n" +
            "  \"modules\": [\n" +
            "    {\n" +
            "      \"name\": \"Finance\",\n" +
            "      \"isSystem\": false,\n" +
            "      \"isActive\": true,\n" +
            "      \"entities\": [\n" +
            "        {\n" +
            "          \"name\": \"Invoice\",\n" +
            "          \"physicalName\": \"OSUSR_FIN_INVOICE\",\n" +
            "          \"isStatic\": false,\n" +
            "          \"isExternal\": false,\n" +
            "          \"isActive\": true,\n" +
            "          \"db_schema\": \"dbo\",\n" +
            "          \"attributes\": [\n" +
            "            {\n" +
            "              \"name\": \"Id\",\n" +
            "              \"physicalName\": \"ID\",\n" +
            "              \"dataType\": \"Identifier\",\n" +
            "              \"isMandatory\": true,\n" +
            "              \"isIdentifier\": true,\n" +
            "              \"isAutoNumber\": true,\n" +
            "              \"isActive\": true\n" +
            "            }\n" +
            "          ]\n" +
            "        }\n" +
            "      ]\n" +
            "    }\n" +
            "  ]\n" +
            "}\n";

        var deserializer = new ModelJsonDeserializer();
        var fileSystem = CreateFileSystem(path, json);
        var service = new ModelIngestionService(deserializer, fileSystem);

        var result = await service.LoadFromFileAsync(path);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Message)));
        Assert.Single(result.Value.Modules);
        Assert.Equal("Finance", result.Value.Modules[0].Name.Value);
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldFail_WhenFileMissing()
    {
        var deserializer = new ModelJsonDeserializer();
        var fileSystem = new MockFileSystem();
        var service = new ModelIngestionService(deserializer, fileSystem);

        var result = await service.LoadFromFileAsync("/missing.json");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "ingestion.path.notFound");
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldFail_WhenJsonMalformed()
    {
        const string path = @"/data/model.json";
        var fileSystem = CreateFileSystem(path, "{");
        var deserializer = new ModelJsonDeserializer();
        var service = new ModelIngestionService(deserializer, fileSystem);

        var result = await service.LoadFromFileAsync(path);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "json.parse.failed");
    }
}
