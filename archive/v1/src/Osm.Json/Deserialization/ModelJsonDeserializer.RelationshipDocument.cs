using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record RelationshipDocument
    {
        [JsonPropertyName("viaAttributeName")]
        public string? ViaAttributeName { get; init; }

        [JsonPropertyName("toEntity_name")]
        public string? TargetEntityName { get; init; }

        [JsonPropertyName("toEntity_physicalName")]
        public string? TargetEntityPhysicalName { get; init; }

        [JsonPropertyName("deleteRuleCode")]
        public string? DeleteRuleCode { get; init; }

        [JsonPropertyName("hasDbConstraint")]
        public int? HasDbConstraint { get; init; }

        [JsonPropertyName("actualConstraints")]
        public RelationshipConstraintDocument[]? ActualConstraints { get; init; }
    }

    internal sealed record RelationshipConstraintDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("referencedSchema")]
        public string? ReferencedSchema { get; init; }

        [JsonPropertyName("referencedTable")]
        public string? ReferencedTable { get; init; }

        [JsonPropertyName("onDelete")]
        public string? OnDelete { get; init; }

        [JsonPropertyName("onUpdate")]
        public string? OnUpdate { get; init; }

        [JsonPropertyName("columns")]
        public RelationshipConstraintColumnDocument[]? Columns { get; init; }
    }

    internal sealed record RelationshipConstraintColumnDocument
    {
        [JsonPropertyName("ordinal")]
        public int Ordinal { get; init; }

        [JsonPropertyName("owner.physical")]
        public string? OwnerPhysical { get; init; }

        [JsonPropertyName("owner.attribute")]
        public string? OwnerAttribute { get; init; }

        [JsonPropertyName("referenced.physical")]
        public string? ReferencedPhysical { get; init; }

        [JsonPropertyName("referenced.attribute")]
        public string? ReferencedAttribute { get; init; }
    }
}
