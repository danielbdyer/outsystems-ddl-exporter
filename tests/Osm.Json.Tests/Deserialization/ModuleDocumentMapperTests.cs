using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.ValueObjects;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

using AttributeDocument = ModelJsonDeserializer.AttributeDocument;
using EntityDocument = ModelJsonDeserializer.EntityDocument;
using ModuleDocument = ModelJsonDeserializer.ModuleDocument;

public class ModuleDocumentMapperTests
{
    private static DocumentMapperContext CreateContext(List<string> warnings)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return new DocumentMapperContext(ModelJsonDeserializerOptions.Default, warnings, serializerOptions);
    }

    private static AttributeDocument CreateAttribute(string name, bool isIdentifier)
        => new()
        {
            Name = name,
            PhysicalName = name.ToUpperInvariant(),
            DataType = "Identifier",
            IsMandatory = true,
            IsIdentifier = isIdentifier,
            IsAutoNumber = isIdentifier,
            IsActive = true,
            ExtendedProperties = Array.Empty<ModelJsonDeserializer.ExtendedPropertyDocument>()
        };

    private static EntityDocument CreateEntity(string name)
        => new()
        {
            Name = name,
            PhysicalName = $"OSUSR_{name.ToUpperInvariant()}",
            Schema = "dbo",
            Catalog = "catalog",
            IsActive = true,
            Attributes = new[] { CreateAttribute("Id", true) }
        };

    [Fact]
    public void Map_ShouldProjectActiveEntities()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var entityMapper = new EntityDocumentMapper(context, attributeMapper, extendedPropertyMapper);
        var moduleMapper = new ModuleDocumentMapper(context, entityMapper, extendedPropertyMapper);

        var moduleDocument = new ModuleDocument
        {
            Name = "Finance",
            Entities = new[] { CreateEntity("Invoice") }
        };

        var result = moduleMapper.Map(
            moduleDocument,
            ModuleName.Create("Finance").Value,
            DocumentPathContext.Root.Property("modules").Index(0));

        Assert.True(result.IsSuccess);
        var module = result.Value!;
        Assert.NotNull(module);
        Assert.Equal("Finance", module.Name.Value);
        var entity = Assert.Single(module.Entities);
        Assert.Equal("Invoice", entity.LogicalName.Value);
    }

    [Fact]
    public void Map_ShouldReturnNull_WhenAllEntitiesInactive()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var entityMapper = new EntityDocumentMapper(context, attributeMapper, extendedPropertyMapper);
        var moduleMapper = new ModuleDocumentMapper(context, entityMapper, extendedPropertyMapper);

        var moduleDocument = new ModuleDocument
        {
            Name = "Finance",
            Entities = new[]
            {
                new EntityDocument
                {
                    Name = "Legacy",
                    PhysicalName = "OSUSR_LEGACY",
                    Schema = "dbo",
                    IsActive = false,
                    Attributes = Array.Empty<AttributeDocument>()
                }
            }
        };

        var result = moduleMapper.Map(
            moduleDocument,
            ModuleName.Create("Finance").Value,
            DocumentPathContext.Root.Property("modules").Index(0));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(warnings, message => message.Contains("contains no entities"));
    }

    [Fact]
    public void Map_ShouldFail_WhenEntityEntryIsNull()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var entityMapper = new EntityDocumentMapper(context, attributeMapper, extendedPropertyMapper);
        var moduleMapper = new ModuleDocumentMapper(context, entityMapper, extendedPropertyMapper);

        var moduleDocument = new ModuleDocument
        {
            Name = "Finance",
            Entities = new EntityDocument[] { null! }
        };

        var result = moduleMapper.Map(
            moduleDocument,
            ModuleName.Create("Finance").Value,
            DocumentPathContext.Root.Property("modules").Index(0));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("module.entities.null", error.Code);
        Assert.Contains("Path: $['modules'][0]['entities'][0]", error.Message);
    }
}
