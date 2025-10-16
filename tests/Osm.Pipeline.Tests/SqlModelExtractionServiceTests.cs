using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Json;
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
        AssertModulesMatch(json, result.Value.Json);
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
        using var payload = JsonDocument.Parse(result.Value.Json);
        var modules = payload.RootElement.GetProperty("modules");
        Assert.Equal(JsonValueKind.Array, modules.ValueKind);
        Assert.Equal(0, modules.GetArrayLength());
        Assert.Contains(result.Value.Warnings, warning =>
            warning.Contains("no modules", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtractAsync_ShouldSurfaceWarningsForModulesWithoutEntities()
    {
        const string json = "{\n" +
            "  \"exportedAtUtc\": \"2025-01-01T00:00:00Z\",\n" +
            "  \"modules\": [\n" +
            "    {\n" +
            "      \"name\": \"EmptyModule\",\n" +
            "      \"isSystem\": false,\n" +
            "      \"isActive\": true,\n" +
            "      \"entities\": []\n" +
            "    },\n" +
            "    {\n" +
            "      \"name\": \"Inventory\",\n" +
            "      \"isSystem\": false,\n" +
            "      \"isActive\": true,\n" +
            "      \"entities\": [\n" +
            "        {\n" +
            "          \"name\": \"Product\",\n" +
            "          \"physicalName\": \"OSUSR_INV_PRODUCT\",\n" +
            "          \"db_schema\": \"dbo\",\n" +
            "          \"isStatic\": false,\n" +
            "          \"isExternal\": false,\n" +
            "          \"isActive\": true,\n" +
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
            "          ],\n" +
            "          \"indexes\": [],\n" +
            "          \"relationships\": []\n" +
            "        }\n" +
            "      ]\n" +
            "    }\n" +
            "  ]\n" +
            "}\n";

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

    private sealed class StubMetadataReader : IOutsystemsMetadataReader
    {
        private readonly Result<OutsystemsMetadataSnapshot> _result;

        public StubMetadataReader(Result<OutsystemsMetadataSnapshot> result)
        {
            _result = result;
        }

        public Task<Result<OutsystemsMetadataSnapshot>> ReadAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
