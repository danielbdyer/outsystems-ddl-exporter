using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record SequenceDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("dataType")]
        public string? DataType { get; init; }

        [JsonPropertyName("startValue")]
        public long? StartValue { get; init; }

        [JsonPropertyName("increment")]
        public long? Increment { get; init; }

        [JsonPropertyName("minValue")]
        public long? MinValue { get; init; }

        [JsonPropertyName("maxValue")]
        public long? MaxValue { get; init; }

        [JsonPropertyName("cycle")]
        public bool Cycle { get; init; }

        [JsonPropertyName("cacheMode")]
        public string? CacheMode { get; init; }

        [JsonPropertyName("cacheSize")]
        public int? CacheSize { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }
}
