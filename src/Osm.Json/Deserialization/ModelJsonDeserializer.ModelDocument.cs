using System;
using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record ModelDocument
    {
        [JsonPropertyName("exportedAtUtc")]
        public DateTime ExportedAtUtc { get; init; }

        [JsonPropertyName("modules")]
        public ModuleDocument[]? Modules { get; init; }

        [JsonPropertyName("sequences")]
        public SequenceDocument[]? Sequences { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }
}
