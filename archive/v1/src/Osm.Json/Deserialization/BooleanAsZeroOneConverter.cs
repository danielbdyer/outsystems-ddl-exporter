using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Osm.Json.Deserialization;

internal sealed class BooleanAsZeroOneConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number when reader.TryGetInt32(out var numericValue):
                if (numericValue is 0 or 1)
                {
                    return numericValue;
                }

                break;
            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    break;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is 0 or 1)
                {
                    return parsed;
                }

                if (bool.TryParse(text, out var boolValue))
                {
                    return boolValue ? 1 : 0;
                }

                break;
        }

        throw new JsonException("Expected boolean or 0/1 value.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
