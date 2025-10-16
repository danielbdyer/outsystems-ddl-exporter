using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

using ExtendedPropertyDocument = ModelJsonDeserializer.ExtendedPropertyDocument;
using SequenceDocument = ModelJsonDeserializer.SequenceDocument;

public class SequenceDocumentMapperTests
{
    private static DocumentMapperContext CreateContext()
    {
        var serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return new DocumentMapperContext(ModelJsonDeserializerOptions.Default, new List<string>(), serializerOptions);
    }

    private static ExtendedPropertyDocument CreateProperty(string name, string value)
    {
        using var document = JsonDocument.Parse($"\"{value}\"");
        return new ExtendedPropertyDocument
        {
            Name = name,
            Value = document.RootElement.Clone()
        };
    }

    [Fact]
    public void Map_ShouldProduceSequenceModel()
    {
        var context = CreateContext();
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var mapper = new SequenceDocumentMapper(context, extendedPropertyMapper);

        var document = new SequenceDocument
        {
            Schema = "dbo",
            Name = "SEQ_INVOICE",
            DataType = "bigint",
            StartValue = 1,
            Increment = 1,
            MinValue = 1,
            MaxValue = 100,
            CacheMode = "cache",
            CacheSize = 32,
            ExtendedProperties = new[] { CreateProperty("sequence.owner", "Finance") }
        };

        var result = mapper.Map(new[] { document }, DocumentPathContext.Root.Property("sequences"));

        Assert.True(result.IsSuccess);
        var sequence = Assert.Single(result.Value);
        Assert.Equal("SEQ_INVOICE", sequence.Name.Value);
        Assert.Equal(32, sequence.CacheSize);
        var property = Assert.Single(sequence.ExtendedProperties);
        Assert.Equal("sequence.owner", property.Name);
        Assert.Equal("Finance", property.Value);
    }
}
