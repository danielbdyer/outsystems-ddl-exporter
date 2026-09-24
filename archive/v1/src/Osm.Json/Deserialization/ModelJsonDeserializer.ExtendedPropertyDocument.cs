using System.Text.Json;
using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record ExtendedPropertyDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("value")]
        public JsonElement Value { get; init; }
    }
}
