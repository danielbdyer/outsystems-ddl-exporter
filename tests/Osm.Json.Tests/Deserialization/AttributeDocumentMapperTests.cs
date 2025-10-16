using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

using AttributeDocument = ModelJsonDeserializer.AttributeDocument;

public class AttributeDocumentMapperTests
{
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
        var mapper = new AttributeDocumentMapper(new ExtendedPropertyDocumentMapper());

        var result = mapper.Map(new[] { CreateAttribute("Id") });

        Assert.True(result.IsSuccess);
        var attribute = Assert.Single(result.Value);
        Assert.Equal("Id", attribute.LogicalName.Value);
        Assert.True(attribute.IsIdentifier);
    }

    [Fact]
    public void Map_ShouldFail_WhenNameIsInvalid()
    {
        var mapper = new AttributeDocumentMapper(new ExtendedPropertyDocumentMapper());

        var result = mapper.Map(new[] { CreateAttribute(string.Empty) });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code.Contains("attribute"));
    }
}
