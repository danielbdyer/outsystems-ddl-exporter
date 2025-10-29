using System.Text.Json;
using Osm.Json.Deserialization;
using Xunit;

namespace Osm.Json.Tests.Deserialization;

public class MetaDescriptionReaderTests
{
    [Fact]
    public void Extract_ShouldLocateDescriptionProperty()
    {
        const string json = "{\"description\":{\"value\":\"Document meta\"}}";
        using var document = JsonDocument.Parse(json);
        var description = MetaDescriptionReader.Extract(document.RootElement);
        Assert.Equal("Document meta", description);
    }

    [Fact]
    public void Extract_ShouldSearchNestedStructures()
    {
        const string json = "{\"items\":[{\"name\":\"description\",\"value\":{\"text\":\"Nested\"}}]}";
        using var document = JsonDocument.Parse(json);
        var description = MetaDescriptionReader.Extract(document.RootElement);
        Assert.Equal("Nested", description);
    }

    [Fact]
    public void Normalize_ShouldReturnNullForWhitespace()
    {
        Assert.Null(MetaDescriptionReader.Normalize(" "));
    }
}
