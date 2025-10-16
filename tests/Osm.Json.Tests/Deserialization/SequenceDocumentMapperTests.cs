using System.Text.Json;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

using ExtendedPropertyDocument = ModelJsonDeserializer.ExtendedPropertyDocument;
using SequenceDocument = ModelJsonDeserializer.SequenceDocument;

public class SequenceDocumentMapperTests
{
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
        var mapper = new SequenceDocumentMapper(new ExtendedPropertyDocumentMapper());

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

        var result = mapper.Map(new[] { document });

        Assert.True(result.IsSuccess);
        var sequence = Assert.Single(result.Value);
        Assert.Equal("SEQ_INVOICE", sequence.Name.Value);
        Assert.Equal(32, sequence.CacheSize);
        var property = Assert.Single(sequence.ExtendedProperties);
        Assert.Equal("sequence.owner", property.Name);
        Assert.Equal("Finance", property.Value);
    }
}
