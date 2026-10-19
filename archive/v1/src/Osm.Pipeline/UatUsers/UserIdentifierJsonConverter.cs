using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Osm.Pipeline.UatUsers;

internal sealed class UserIdentifierJsonConverter : JsonConverter<UserIdentifier>
{
    public override UserIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (value is null)
            {
                throw new JsonException("User identifier value cannot be null.");
            }

            return UserIdentifier.FromString(value);
        }

        throw new JsonException($"Unexpected token {reader.TokenType} when parsing a user identifier.");
    }

    public override void Write(Utf8JsonWriter writer, UserIdentifier value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
