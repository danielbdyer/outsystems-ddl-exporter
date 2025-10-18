using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;

namespace Osm.Pipeline.Tests;

public class SqlModelExtractionServiceTests
{
    [Fact]
    public void CreateCommand_ShouldRejectNullModule()
    {
        var result = ModelExtractionCommand.Create(new[] { "AppCore", null! }, includeSystemModules: false, onlyActiveAttributes: false);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.modules.null", error.Code);
    }

    [Fact]
    public void CreateCommand_ShouldRejectEmptyModule()
    {
        var result = ModelExtractionCommand.Create(new[] { "  " }, includeSystemModules: false, onlyActiveAttributes: false);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.modules.empty", error.Code);
    }

    [Fact]
    public void CreateCommand_ShouldSortAndDeduplicateModules()
    {
        var result = ModelExtractionCommand.Create(new[] { "Ops", "AppCore", "ops" }, includeSystemModules: true, onlyActiveAttributes: false);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "AppCore", "Ops" }, result.Value.ModuleNames.Select(static module => module.Value));
        Assert.True(result.Value.IncludeSystemModules);
        Assert.False(result.Value.OnlyActiveAttributes);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnModelAndJson()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.micro-unique.json"));
        var snapshot = CreateSnapshotFromJson(json);
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Model);
        var payloadJson = await result.Value.JsonPayload.ReadAsStringAsync();
        AssertModulesMatch(json, payloadJson);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPropagateMetadataFailure()
    {
        var failure = Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create("boom", "fail"));
        var reader = new StubMetadataReader(failure);
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("boom", error.Code);
    }

    [Fact]
    public async Task ExtractAsync_ShouldHandleEmptyModuleSnapshot()
    {
        var emptySnapshot = new OutsystemsMetadataSnapshot(
            Array.Empty<OutsystemsModuleRow>(),
            Array.Empty<OutsystemsEntityRow>(),
            Array.Empty<OutsystemsAttributeRow>(),
            Array.Empty<OutsystemsReferenceRow>(),
            Array.Empty<OutsystemsPhysicalTableRow>(),
            Array.Empty<OutsystemsColumnRealityRow>(),
            Array.Empty<OutsystemsColumnCheckRow>(),
            Array.Empty<OutsystemsColumnCheckJsonRow>(),
            Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Array.Empty<OutsystemsIndexRow>(),
            Array.Empty<OutsystemsIndexColumnRow>(),
            Array.Empty<OutsystemsForeignKeyRow>(),
            Array.Empty<OutsystemsForeignKeyColumnRow>(),
            Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            Array.Empty<OutsystemsAttributeHasFkRow>(),
            Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Array.Empty<OutsystemsTriggerRow>(),
            Array.Empty<OutsystemsAttributeJsonRow>(),
            Array.Empty<OutsystemsRelationshipJsonRow>(),
            Array.Empty<OutsystemsIndexJsonRow>(),
            Array.Empty<OutsystemsTriggerJsonRow>(),
            Array.Empty<OutsystemsModuleJsonRow>(),
            "Fixture");

        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(emptySnapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);
        Assert.True(result.IsSuccess);
        var jsonText = await result.Value.JsonPayload.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(jsonText);
        var modules = payload.RootElement.GetProperty("modules");
        Assert.Equal(JsonValueKind.Array, modules.ValueKind);
        Assert.Equal(0, modules.GetArrayLength());
        Assert.Contains(result.Value.Warnings, warning =>
            warning.Contains("no modules", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtractAsync_ShouldSurfaceWarningsForModulesWithoutEntities()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "EmptyModule",
              "isSystem": false,
              "isActive": true,
              "entities": []
            },
            {
              "name": "Inventory",
              "isSystem": false,
              "isActive": true,
              "entities": [
                {
                  "name": "Product",
                  "physicalName": "OSUSR_INV_PRODUCT",
                  "db_schema": "dbo",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
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
                  ],
                  "indexes": [],
                  "relationships": []
                }
              ]
            }
          ]
        }
        """;

        var snapshot = CreateSnapshotFromJson(json);
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Contains("EmptyModule", warning);
        var module = Assert.Single(result.Value.Model.Modules);
        Assert.Equal("Inventory", module.Name.Value);
    }

    [Fact]
    public async Task ExtractAsync_ShouldMatchLegacyJsonSnapshot()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.edge-case.json"));
        var snapshot = CreateSnapshotFromJson(json);
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: true, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);

        Assert.True(result.IsSuccess);

        var actualJson = await result.Value.JsonPayload.ReadAsStringAsync();
        using var actualDocument = JsonDocument.Parse(actualJson);
        var exportedAtUtc = actualDocument.RootElement.GetProperty("exportedAtUtc").GetDateTime();
        var expectedJson = BuildLegacyJson(snapshot, exportedAtUtc);

        using var expectedDocument = JsonDocument.Parse(expectedJson);

        Assert.Equal(expectedDocument.RootElement.GetRawText(), actualDocument.RootElement.GetRawText());
    }

    [Fact]
    public async Task ExtractAsync_ShouldExposeUserReferencesWhenSystemModulesExcluded()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.edge-case.json"));
        var snapshot = CreateSnapshotFromJson(json);
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(
            new[] { "AppCore", "ExtBilling", "Ops" },
            includeSystemModules: false,
            onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);

        Assert.True(result.IsSuccess);
        var model = result.Value.Model;
        var opsModule = Assert.Single(model.Modules.Where(module
            => module.Name.Value.Equals("Ops", StringComparison.OrdinalIgnoreCase)));
        var jobRun = Assert.Single(opsModule.Entities.Where(entity
            => entity.LogicalName.Value.Equals("JobRun", StringComparison.OrdinalIgnoreCase)));
        var triggeredBy = Assert.Single(jobRun.Attributes.Where(attribute
            => attribute.LogicalName.Value.Equals("TriggeredByUserId", StringComparison.OrdinalIgnoreCase)));

        Assert.True(triggeredBy.Reference.IsReference);
        Assert.Equal("User", triggeredBy.Reference.TargetEntity?.Value);
        Assert.Equal("OSUSR_U_USER", triggeredBy.Reference.TargetPhysicalName?.Value);
    }

    [Fact]
    public async Task ExtractAsync_ShouldSupportCustomDestinationStream()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.micro-unique.json"));
        var snapshot = CreateSnapshotFromJson(json);
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        await using var destination = new MemoryStream();
        var result = await service.ExtractAsync(command, ModelExtractionOptions.ToStream(destination));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, destination.Position);
        Assert.True(destination.Length > 0);

        var firstRead = await result.Value.JsonPayload.ReadAsStringAsync();
        Assert.Equal(0, destination.Position);
        var bufferSnapshot = Encoding.UTF8.GetString(destination.ToArray());
        Assert.Equal(bufferSnapshot, firstRead);
        var secondRead = await result.Value.JsonPayload.ReadAsStringAsync();
        Assert.Equal(firstRead, secondRead);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPersistJsonToFilePath()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.micro-unique.json"));
        var snapshot = CreateSnapshotFromJson(json);
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "model.json");

        var result = await service.ExtractAsync(command, ModelExtractionOptions.ToFile(path));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.JsonPayload.IsPersisted);
        Assert.Equal(Path.GetFullPath(path), result.Value.JsonPayload.FilePath);
        Assert.True(File.Exists(path));

        var diskContents = await File.ReadAllTextAsync(path);
        var payloadContents = await result.Value.JsonPayload.ReadAsStringAsync();
        Assert.Equal(diskContents, payloadContents);
    }

    [Fact]
    public async Task ExtractAsync_ShouldWriteMetadataSnapshotWhenPathProvided()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.micro-unique.json"));
        var snapshot = CreateSnapshotFromJson(json);
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        using var temp = new TempDirectory();
        var metadataPath = Path.Combine(temp.Path, "metadata.json");

        var metadataLog = new SqlMetadataLog();
        var result = await service.ExtractAsync(command, ModelExtractionOptions.InMemory(metadataPath, metadataLog));

        Assert.True(result.IsSuccess);

        var state = metadataLog.BuildState();
        Assert.True(state.HasSnapshot);
        Assert.Equal(snapshot.DatabaseName, state.DatabaseName);
        Assert.Equal(snapshot.Modules, state.Snapshot!.Modules);
        Assert.True(state.HasRequests);
    }

    [Fact]
    public async Task ExtractAsync_ShouldWriteFailureSnapshotWhenMetadataReaderFails()
    {
        var failureError = ValidationError.Create("boom", "fail");
        var failure = Result<OutsystemsMetadataSnapshot>.Failure(failureError);
        var column = new MetadataColumnSnapshot(
            ordinal: 0,
            name: "AttrId",
            providerType: "int",
            isNull: false,
            rawValue: 5,
            valuePreview: MetadataColumnSnapshot.FormatValuePreview(5, 32),
            serializationError: null);
        var rowSnapshot = new MetadataRowSnapshot("AttributesJson", 3, new[] { column });
        var reader = new StubMetadataReader(failure, rowSnapshot);
        var service = new SqlModelExtractionService(reader, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        using var temp = new TempDirectory();
        var metadataPath = Path.Combine(temp.Path, "metadata.json");

        var metadataLog = new SqlMetadataLog();
        var result = await service.ExtractAsync(command, ModelExtractionOptions.InMemory(metadataPath, metadataLog));

        Assert.True(result.IsFailure);

        var state = metadataLog.BuildState();
        var error = Assert.Single(state.Errors);
        Assert.True(state.HasErrors);
        Assert.NotNull(state.FailureRowSnapshot);
        Assert.Equal("AttributesJson", state.FailureRowSnapshot!.ResultSetName);
        Assert.Equal(3, state.FailureRowSnapshot.RowIndex);
    }

    private static OutsystemsMetadataSnapshot CreateSnapshotFromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var modules = document.RootElement.GetProperty("modules");
        var moduleRows = new List<OutsystemsModuleJsonRow>();
        foreach (var module in modules.EnumerateArray())
        {
            var name = module.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
            var isSystem = module.TryGetProperty("isSystem", out var systemElement) && systemElement.GetBoolean();
            var isActive = module.TryGetProperty("isActive", out var activeElement) ? activeElement.GetBoolean() : true;
            var entitiesJson = module.TryGetProperty("entities", out var entitiesElement)
                ? entitiesElement.GetRawText()
                : "[]";

            moduleRows.Add(new OutsystemsModuleJsonRow(name, isSystem, isActive, entitiesJson));
        }

        return new OutsystemsMetadataSnapshot(
            Array.Empty<OutsystemsModuleRow>(),
            Array.Empty<OutsystemsEntityRow>(),
            Array.Empty<OutsystemsAttributeRow>(),
            Array.Empty<OutsystemsReferenceRow>(),
            Array.Empty<OutsystemsPhysicalTableRow>(),
            Array.Empty<OutsystemsColumnRealityRow>(),
            Array.Empty<OutsystemsColumnCheckRow>(),
            Array.Empty<OutsystemsColumnCheckJsonRow>(),
            Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Array.Empty<OutsystemsIndexRow>(),
            Array.Empty<OutsystemsIndexColumnRow>(),
            Array.Empty<OutsystemsForeignKeyRow>(),
            Array.Empty<OutsystemsForeignKeyColumnRow>(),
            Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            Array.Empty<OutsystemsAttributeHasFkRow>(),
            Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Array.Empty<OutsystemsTriggerRow>(),
            Array.Empty<OutsystemsAttributeJsonRow>(),
            Array.Empty<OutsystemsRelationshipJsonRow>(),
            Array.Empty<OutsystemsIndexJsonRow>(),
            Array.Empty<OutsystemsTriggerJsonRow>(),
            moduleRows,
            "Fixture");
    }

    private static void AssertModulesMatch(string expectedJson, string actualJson)
    {
        using var expectedDoc = JsonDocument.Parse(expectedJson);
        using var actualDoc = JsonDocument.Parse(actualJson);

        var expectedModules = expectedDoc.RootElement.GetProperty("modules");
        var actualModules = actualDoc.RootElement.GetProperty("modules");

        Assert.True(expectedModules.ValueKind == JsonValueKind.Array, "Expected modules array.");
        Assert.True(actualModules.ValueKind == JsonValueKind.Array, "Actual modules array missing.");
        Assert.Equal(expectedModules.GetArrayLength(), actualModules.GetArrayLength());

        for (var i = 0; i < expectedModules.GetArrayLength(); i++)
        {
            using var expectedEntityDoc = JsonDocument.Parse(expectedModules[i].GetRawText());
            using var actualEntityDoc = JsonDocument.Parse(actualModules[i].GetRawText());
            Assert.True(expectedEntityDoc.RootElement.TryGetProperty("name", out var expectedName));
            Assert.True(actualEntityDoc.RootElement.TryGetProperty("name", out var actualName));
            Assert.Equal(expectedName.GetString(), actualName.GetString());
        }
    }

    private static string BuildLegacyJson(OutsystemsMetadataSnapshot snapshot, DateTime exportedAtUtc)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("exportedAtUtc", exportedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WritePropertyName("modules");
        writer.WriteStartArray();

        foreach (var module in snapshot.ModuleJson)
        {
            writer.WriteStartObject();
            writer.WriteString("name", module.ModuleName);
            writer.WriteBoolean("isSystem", module.IsSystem);
            writer.WriteBoolean("isActive", module.IsActive);
            writer.WritePropertyName("entities");

            var entitiesPayload = string.IsNullOrWhiteSpace(module.ModuleEntitiesJson)
                ? "[]"
                : module.ModuleEntitiesJson;

            writer.WriteRawValue(entitiesPayload, skipInputValidation: true);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class StubMetadataReader : IOutsystemsMetadataReader, IMetadataSnapshotDiagnostics
    {
        private readonly Result<OutsystemsMetadataSnapshot> _result;
        private readonly MetadataRowSnapshot? _failureSnapshot;

        public StubMetadataReader(Result<OutsystemsMetadataSnapshot> result, MetadataRowSnapshot? failureSnapshot = null)
        {
            _result = result;
            _failureSnapshot = failureSnapshot;
        }

        public Task<Result<OutsystemsMetadataSnapshot>> ReadAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public MetadataRowSnapshot? LastFailureRowSnapshot => _result.IsFailure ? _failureSnapshot : null;
    }
}
