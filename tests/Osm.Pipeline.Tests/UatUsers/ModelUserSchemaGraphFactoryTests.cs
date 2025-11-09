using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.UatUsers;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class ModelUserSchemaGraphFactoryTests
{
    [Fact]
    public async Task Create_UsesDatasetJsonWhenAvailable()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var jsonPath = FixtureFile.GetPath(Path.Combine("model", "model.edge-case.json"));
        var json = File.ReadAllText(jsonPath);
        var dataset = CreateDataset(json);
        var extraction = CreateExtraction(model, dataset);
        var factory = new ModelUserSchemaGraphFactory();

        var result = factory.Create(extraction);

        Assert.True(result.IsSuccess);
        var expectedGraph = new ModelSchemaGraph(model);
        var expected = await expectedGraph.GetForeignKeysAsync(CancellationToken.None);
        var actual = await result.Value.GetForeignKeysAsync(CancellationToken.None);
        Assert.Equal(expected.Count, actual.Count);
    }

    [Fact]
    public async Task Create_FallsBackToModelWhenDatasetHasNoJson()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var dataset = CreateNumericDataset();
        var extraction = CreateExtraction(model, dataset);
        var factory = new ModelUserSchemaGraphFactory();

        var result = factory.Create(extraction);

        Assert.True(result.IsSuccess);
        var expectedGraph = new ModelSchemaGraph(model);
        var expected = await expectedGraph.GetForeignKeysAsync(CancellationToken.None);
        var actual = await result.Value.GetForeignKeysAsync(CancellationToken.None);
        Assert.Equal(expected.Count, actual.Count);
    }

    private static DynamicEntityDataset CreateDataset(string json)
    {
        var definition = new StaticEntitySeedTableDefinition(
            "Metadata",
            "ModelJson",
            "dbo",
            "OSMODEL_JSON",
            "OSMODEL_JSON",
            ImmutableArray.Create(
                new StaticEntitySeedColumn(
                    "Payload",
                    "Payload",
                    "Payload",
                    "NVARCHAR",
                    null,
                    null,
                    null,
                    IsPrimaryKey: false,
                    IsIdentity: false)));

        var row = StaticEntityRow.Create(new object?[] { json });
        var table = new StaticEntityTableData(definition, ImmutableArray.Create(row));
        return new DynamicEntityDataset(ImmutableArray.Create(table));
    }

    private static DynamicEntityDataset CreateNumericDataset()
    {
        var definition = new StaticEntitySeedTableDefinition(
            "Metadata",
            "Numbers",
            "dbo",
            "OSMODEL_NUMBERS",
            "OSMODEL_NUMBERS",
            ImmutableArray.Create(
                new StaticEntitySeedColumn(
                    "Value",
                    "Value",
                    "Value",
                    "INT",
                    null,
                    null,
                    null,
                    IsPrimaryKey: false,
                    IsIdentity: false)));

        var row = StaticEntityRow.Create(new object?[] { 42 });
        var table = new StaticEntityTableData(definition, ImmutableArray.Create(row));
        return new DynamicEntityDataset(ImmutableArray.Create(table));
    }

    private static ModelExtractionResult CreateExtraction(Osm.Domain.Model.OsmModel model, DynamicEntityDataset dataset)
    {
        var payloadStream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        var payload = ModelJsonPayload.FromStream(payloadStream);
        var metadata = new OutsystemsMetadataSnapshot(
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
            "(fixture)");

        return new ModelExtractionResult(
            model,
            payload,
            DateTimeOffset.UtcNow,
            ImmutableArray<string>.Empty,
            metadata,
            dataset);
    }
}
