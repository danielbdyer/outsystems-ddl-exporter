using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

using AttributeDocument = ModelJsonDeserializer.AttributeDocument;

public class AttributeDocumentMapperTests
{
    private static DocumentMapperContext CreateContext()
    {
        var serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return new DocumentMapperContext(ModelJsonDeserializerOptions.Default, new List<string>(), serializerOptions);
    }

    private static AttributeDocument CreateAttribute(string name)
        => new()
        {
            Name = name,
            PhysicalName = name.ToUpperInvariant(),
            DataType = "Identifier",
            IsMandatory = true,
            IsIdentifier = true,
            IsAutoNumber = true,
            IsActive = true,
            ExtendedProperties = Array.Empty<ModelJsonDeserializer.ExtendedPropertyDocument>()
        };

    [Fact]
    public void Map_ShouldProduceAttributeModel()
    {
        var context = CreateContext();
        var mapper = new AttributeDocumentMapper(context, new ExtendedPropertyDocumentMapper(context));

        var result = mapper.Map(new[] { CreateAttribute("Id") }, DocumentPathContext.Root.Property("attributes"));

        Assert.True(result.IsSuccess);
        var attribute = Assert.Single(result.Value);
        Assert.Equal("Id", attribute.LogicalName.Value);
        Assert.True(attribute.IsIdentifier);
    }

    [Fact]
    public void Map_ShouldFail_WhenNameIsInvalid()
    {
        var context = CreateContext();
        var mapper = new AttributeDocumentMapper(context, new ExtendedPropertyDocumentMapper(context));

        var result = mapper.Map(new[] { CreateAttribute(string.Empty) }, DocumentPathContext.Root.Property("attributes"));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code.Contains("attribute"));
        Assert.Contains(result.Errors, error => error.Message.Contains("Path: $['attributes'][0]['name']", StringComparison.Ordinal));
    }

    [Fact]
    public void Map_ShouldFail_WhenAttributeCollectionContainsNullEntry()
    {
        var context = CreateContext();
        var mapper = new AttributeDocumentMapper(context, new ExtendedPropertyDocumentMapper(context));

        var result = mapper.Map(new AttributeDocument[] { null! }, DocumentPathContext.Root.Property("attributes"));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("entity.attributes.nullEntry", error.Code);
        Assert.Contains("$['attributes'][0]", error.Message);
    }
}
