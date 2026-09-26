using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

public class TriggerDocumentMapperTests
{
    private static DocumentMapperContext CreateContext(List<string> warnings)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return new DocumentMapperContext(ModelJsonDeserializerOptions.Default, warnings, serializerOptions);
    }

    [Fact]
    public void Map_ShouldReturnEmpty_WhenNoTriggers()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var mapper = new TriggerDocumentMapper(context);

        var result = mapper.Map(null, DocumentPathContext.Root.Property("triggers"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Map_ShouldFail_WhenNameInvalid()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var mapper = new TriggerDocumentMapper(context);

        var trigger = new ModelJsonDeserializer.TriggerDocument
        {
            Name = "",
            Definition = "SELECT 1"
        };

        var result = mapper.Map(new[] { trigger }, DocumentPathContext.Root.Property("triggers"));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("trigger.name.invalid", error.Code);
    }
}
