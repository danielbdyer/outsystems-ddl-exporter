using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Osm.Json.Deserialization;

internal sealed class AttributeMetaDescriptionConverter : JsonConverter<ModelJsonDeserializer.AttributeMetaDocument?>
{
    public override ModelJsonDeserializer.AttributeMetaDocument? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        var description = MetaDescriptionReader.Normalize(MetaDescriptionReader.Read(ref reader));
        return new ModelJsonDeserializer.AttributeMetaDocument { Description = description };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ModelJsonDeserializer.AttributeMetaDocument? value,
        JsonSerializerOptions options)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Description))
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Description);
    }
}
