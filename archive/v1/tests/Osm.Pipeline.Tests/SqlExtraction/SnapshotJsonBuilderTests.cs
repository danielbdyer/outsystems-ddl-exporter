using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;

namespace Osm.Pipeline.Tests.SqlExtraction;

public class SnapshotJsonBuilderTests
{
    [Fact]
    public async Task Build_ShouldWriteSnapshotJson()
    {
        var builder = new SnapshotJsonBuilder();
        var snapshot = CreateSnapshot();
        var exportedAt = new DateTime(2024, 12, 15, 8, 30, 0, DateTimeKind.Utc);

        await using var artifact = builder.Build(snapshot, exportedAt, ModelExtractionOptions.InMemory());
        artifact.Stream.Position = 0;

        using var document = await JsonDocument.ParseAsync(artifact.Stream);
        var root = document.RootElement;
        Assert.Equal(exportedAt.ToString("O"), root.GetProperty("exportedAtUtc").GetString());
        var module = Assert.Single(root.GetProperty("modules").EnumerateArray());
        Assert.Equal("Inventory", module.GetProperty("name").GetString());
        var entity = Assert.Single(module.GetProperty("entities").EnumerateArray());
        Assert.Equal("Product", entity.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Array, entity.GetProperty("attributes").ValueKind);
    }

    [Fact]
    public async Task Build_ToFile_ShouldPersistSnapshot()
    {
        var builder = new SnapshotJsonBuilder();
        var snapshot = CreateSnapshot();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "snapshot.json");

        await using var artifact = builder.Build(snapshot, DateTime.UtcNow, ModelExtractionOptions.ToFile(path));

        Assert.Equal(Path.GetFullPath(path), artifact.FilePath);
        Assert.True(File.Exists(path));

        var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("modules").EnumerateArray().Any());
    }

    private static OutsystemsMetadataSnapshot CreateSnapshot()
    {
        const int moduleId = 1;
        const int entityId = 1;

        return new OutsystemsMetadataSnapshot(
            new[] { new OutsystemsModuleRow(moduleId, "Inventory", false, true, null, null) },
            new[]
            {
                new OutsystemsEntityRow(
                    entityId,
                    "Product",
                    "OSUSR_INV_PRODUCT",
                    moduleId,
                    EntityIsActive: true,
                    IsSystemEntity: false,
                    IsExternalEntity: false,
                    DataKind: null,
                    PrimaryKeySsKey: null,
                    EntitySsKey: null,
                    EntityDescription: null),
            },
            Array.Empty<OutsystemsAttributeRow>(),
            Array.Empty<OutsystemsReferenceRow>(),
            new[] { new OutsystemsPhysicalTableRow(entityId, "dbo", "OSUSR_INV_PRODUCT", 0) },
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
            new[] { new OutsystemsAttributeJsonRow(entityId, "[{\"name\":\"Id\"}]") },
            new[] { new OutsystemsRelationshipJsonRow(entityId, "[]") },
            new[] { new OutsystemsIndexJsonRow(entityId, "[]") },
            new[] { new OutsystemsTriggerJsonRow(entityId, "[]") },
            Array.Empty<OutsystemsModuleJsonRow>(),
            "FixtureDb");
    }
}
