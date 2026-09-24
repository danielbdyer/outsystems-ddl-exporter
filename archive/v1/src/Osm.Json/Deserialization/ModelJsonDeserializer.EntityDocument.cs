using System.Text.Json.Serialization;
using Osm.Domain.Model;
using Osm.Json.Deserialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record EntityDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("physicalName")]
        public string? PhysicalName { get; init; }

        [JsonPropertyName("isStatic")]
        public bool IsStatic { get; init; }

        [JsonPropertyName("isExternal")]
        public bool IsExternal { get; init; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; init; } = true;

        [JsonPropertyName("db_catalog")]
        public string? Catalog { get; init; }

        [JsonPropertyName("db_schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("attributes")]
        public AttributeDocument[]? Attributes { get; init; }

        [JsonPropertyName("indexes")]
        public IndexDocument[]? Indexes { get; init; }

        [JsonPropertyName("relationships")]
        public RelationshipDocument[]? Relationships { get; init; }

        [JsonPropertyName("triggers")]
        public TriggerDocument[]? Triggers { get; init; }

        [JsonPropertyName("meta")]
        [JsonConverter(typeof(EntityMetaDescriptionConverter))]
        public EntityMetaDocument? Meta { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }

        [JsonPropertyName("temporal")]
        public TemporalDocument? Temporal { get; init; }
    }

    internal sealed record TriggerDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("isDisabled")]
        public bool IsDisabled { get; init; }

        [JsonPropertyName("definition")]
        public string? Definition { get; init; }
    }

    internal sealed record AttributeRealityDocument
    {
        [JsonPropertyName("isNullableInDatabase")]
        public bool? IsNullableInDatabase { get; init; }

        [JsonPropertyName("hasNulls")]
        public bool? HasNulls { get; init; }

        [JsonPropertyName("hasDuplicates")]
        public bool? HasDuplicates { get; init; }

        [JsonPropertyName("hasOrphans")]
        public bool? HasOrphans { get; init; }

        public AttributeReality ToDomain() => new(IsNullableInDatabase, HasNulls, HasDuplicates, HasOrphans, false);
    }
}
