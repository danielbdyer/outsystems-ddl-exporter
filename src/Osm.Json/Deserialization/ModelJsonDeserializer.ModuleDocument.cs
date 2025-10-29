using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record ModuleDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("isSystem")]
        public bool IsSystem { get; init; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; init; } = true;

        [JsonPropertyName("entities")]
        public EntityDocument[]? Entities { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }
}
