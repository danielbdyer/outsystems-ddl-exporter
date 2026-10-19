using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record TemporalDocument
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("historyTable")]
        public TemporalHistoryDocument? History { get; init; }

        [JsonPropertyName("periodStartColumn")]
        public string? PeriodStartColumn { get; init; }

        [JsonPropertyName("periodEndColumn")]
        public string? PeriodEndColumn { get; init; }

        [JsonPropertyName("retention")]
        public TemporalRetentionDocument? Retention { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    internal sealed record TemporalHistoryDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    internal sealed record TemporalRetentionDocument
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("unit")]
        public string? Unit { get; init; }

        [JsonPropertyName("value")]
        public int? Value { get; init; }
    }
}
