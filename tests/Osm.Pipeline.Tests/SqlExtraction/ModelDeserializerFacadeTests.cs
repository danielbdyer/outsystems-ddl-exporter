using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;

namespace Osm.Pipeline.Tests.SqlExtraction;

public class ModelDeserializerFacadeTests
{
    [Fact]
    public void Deserialize_ShouldReturnModelAndAppendModuleWarnings()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = CreateSnapshot();
        var deserializer = new StubModelJsonDeserializer
        {
            Handler = (warnings, options) =>
            {
                warnings?.Add("upstream warning");
                return Result<OsmModel>.Success(model);
            }
        };
        var facade = new ModelDeserializerFacade(deserializer, NullLogger<ModelDeserializerFacade>.Instance);
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: false, onlyActiveAttributes: true).Value;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var result = facade.Deserialize(stream, snapshot, command, DateTimeOffset.UtcNow, new[] { "EmptyModule" });

        Assert.True(result.IsSuccess);
        Assert.Same(model, result.Value.Model);
        Assert.Contains("upstream warning", result.Value.Warnings);
        Assert.Contains("EmptyModule", string.Join(' ', result.Value.Warnings));
        Assert.NotNull(deserializer.LastOptions);
        Assert.True(deserializer.LastOptions!.AllowDuplicateAttributeLogicalNames);
        Assert.True(deserializer.LastOptions.AllowDuplicateAttributeColumnNames);
        Assert.True(deserializer.LastOptions.ValidationOverrides.AllowsMissingPrimaryKey("Inventory", "Product"));
    }

    [Fact]
    public void Deserialize_ShouldReturnEmptyModelWhenNoModules()
    {
        var snapshot = CreateSnapshot();
        var deserializer = new StubModelJsonDeserializer
        {
            Handler = (_, _) => Result<OsmModel>.Failure(ValidationError.Create("model.modules.empty", "no modules"))
        };
        var facade = new ModelDeserializerFacade(deserializer, NullLogger<ModelDeserializerFacade>.Instance);
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: false, onlyActiveAttributes: false).Value;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var result = facade.Deserialize(stream, snapshot, command, DateTimeOffset.UtcNow, Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Model.Modules);
        Assert.Contains(result.Value.Warnings, warning => warning.Contains("no modules", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deserialize_ShouldPropagateErrors()
    {
        var snapshot = CreateSnapshot();
        var deserializer = new StubModelJsonDeserializer
        {
            Handler = (_, _) => Result<OsmModel>.Failure(ValidationError.Create("json.parse.failed", "bad"))
        };
        var facade = new ModelDeserializerFacade(deserializer, NullLogger<ModelDeserializerFacade>.Instance);
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: false, onlyActiveAttributes: false).Value;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var result = facade.Deserialize(stream, snapshot, command, DateTimeOffset.UtcNow, Array.Empty<string>());

        Assert.True(result.IsFailure);
        Assert.Equal("json.parse.failed", Assert.Single(result.Errors).Code);
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
            "FixtureDb");
    }

    private sealed class StubModelJsonDeserializer : IModelJsonDeserializer
    {
        public Func<ICollection<string>?, ModelJsonDeserializerOptions?, Result<OsmModel>>? Handler { get; set; }

        public ModelJsonDeserializerOptions? LastOptions { get; private set; }

        public Result<OsmModel> Deserialize(Stream jsonStream, ICollection<string>? warnings = null, ModelJsonDeserializerOptions? options = null)
        {
            LastOptions = options;
            if (Handler is null)
            {
                throw new InvalidOperationException("Handler must be configured before invoking the stub deserializer.");
            }

            return Handler(warnings, options);
        }
    }
}
