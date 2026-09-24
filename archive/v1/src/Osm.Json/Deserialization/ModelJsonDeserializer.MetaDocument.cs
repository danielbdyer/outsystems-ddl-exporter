using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record EntityMetaDocument
    {
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}
