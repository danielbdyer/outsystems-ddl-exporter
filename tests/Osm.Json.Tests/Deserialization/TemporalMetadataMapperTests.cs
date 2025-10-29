using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

public class TemporalMetadataMapperTests
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
    public void Map_ShouldReturnNone_WhenDocumentMissing()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var mapper = new TemporalMetadataMapper(context, extendedPropertyMapper);

        var result = mapper.Map(null, DocumentPathContext.Root.Property("temporal"));

        Assert.True(result.IsSuccess);
        Assert.Equal(TemporalTableMetadata.None, result.Value);
    }

    [Fact]
    public void Map_ShouldProjectRetentionMetadata()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var mapper = new TemporalMetadataMapper(context, extendedPropertyMapper);

        var document = new ModelJsonDeserializer.TemporalDocument
        {
            Type = "systemversioned",
            History = new ModelJsonDeserializer.TemporalHistoryDocument
            {
                Schema = "history",
                Name = "OSUSR_CUSTOMER_HISTORY"
            },
            PeriodStartColumn = "ValidFrom",
            PeriodEndColumn = "ValidTo",
            Retention = new ModelJsonDeserializer.TemporalRetentionDocument
            {
                Kind = "limited",
                Unit = "day",
                Value = 30
            },
            ExtendedProperties = Array.Empty<ModelJsonDeserializer.ExtendedPropertyDocument>()
        };

        var result = mapper.Map(document, DocumentPathContext.Root.Property("temporal"));

        Assert.True(result.IsSuccess);
        Assert.Equal(TemporalTableType.SystemVersioned, result.Value.Type);
        Assert.Equal("history", result.Value.HistorySchema?.Value);
        Assert.Equal("OSUSR_CUSTOMER_HISTORY", result.Value.HistoryTable?.Value);
        Assert.Equal("ValidFrom", result.Value.PeriodStartColumn?.Value);
        Assert.Equal(TemporalRetentionKind.Limited, result.Value.RetentionPolicy.Kind);
        Assert.Equal(TemporalRetentionUnit.Days, result.Value.RetentionPolicy.Unit);
        Assert.Equal(30, result.Value.RetentionPolicy.Value);
    }
}
