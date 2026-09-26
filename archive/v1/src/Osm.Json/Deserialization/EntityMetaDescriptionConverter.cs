using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Osm.Json.Deserialization;

internal sealed class EntityMetaDescriptionConverter : JsonConverter<ModelJsonDeserializer.EntityMetaDocument?>
{
    public override ModelJsonDeserializer.EntityMetaDocument? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        var description = MetaDescriptionReader.Normalize(MetaDescriptionReader.Read(ref reader));
        return new ModelJsonDeserializer.EntityMetaDocument { Description = description };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ModelJsonDeserializer.EntityMetaDocument? value,
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
