using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Sql;
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
          "exportedAtUtc": "2025-01-01T00:00:00Z",
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
        var service = new ModelIngestionService(deserializer, fileSystem: fileSystem);

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
        var service = new ModelIngestionService(deserializer, fileSystem: fileSystem);

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
        var service = new ModelIngestionService(deserializer, fileSystem: fileSystem);

        var result = await service.LoadFromFileAsync(path);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "json.parse.failed");
    }

    [Fact]
    public async Task LoadFromFileAsync_ShouldInvokeHydrator_WhenSqlMetadataProvided()
    {
        const string path = @"/data/model.json";
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
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

        var fileSystem = CreateFileSystem(path, json);
        var deserializer = new ModelJsonDeserializer();
        var hydrator = new RecordingHydrator();
        var service = new ModelIngestionService(deserializer, hydrator, fileSystem);

        var sqlOptions = new ModelIngestionSqlMetadataOptions(
            "Server=(local);Database=OSM",
            new SqlConnectionOptions(null, null, null, null));
        var ingestionOptions = new ModelIngestionOptions(
            ModuleValidationOverrides.Empty,
            MissingSchemaFallback: null,
            SqlMetadata: sqlOptions);

        var result = await service.LoadFromFileAsync(path, options: ingestionOptions);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Message)));
        Assert.True(hydrator.Invoked);
    }

    private sealed class RecordingHydrator : IRelationshipConstraintHydrator
    {
        public bool Invoked { get; private set; }

        public Task<Result<OsmModel>> HydrateAsync(
            OsmModel model,
            ModelIngestionSqlMetadataOptions sqlOptions,
            ICollection<string>? warnings,
            CancellationToken cancellationToken)
        {
            Invoked = true;
            return Task.FromResult(Result<OsmModel>.Success(model));
        }
    }
}
