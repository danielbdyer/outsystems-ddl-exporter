using System.IO.Abstractions.TestingHelpers;
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
        const string json = """
        {
          "modules": [
            {
              "name": "Finance",
              "isSystem": false,
              "isActive": true,
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
                  "db_schema": "dbo",
                  "attributes": [
                    {
                      "name": "Id",
                      "physicalName": "ID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": true,
                      "isAutoNumber": true,
                      "isActive": true
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        var fileSystem = CreateFileSystem(path, json);
        var service = new ModelIngestionService(deserializer, fileSystem);

        var result = await service.LoadFromFileAsync(path);

        Assert.True(result.IsSuccess);
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
