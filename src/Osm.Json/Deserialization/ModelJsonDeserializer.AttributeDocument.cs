using System.Linq;
using System.Text.Json.Serialization;
using Osm.Domain.Model;
using Osm.Json.Deserialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    internal sealed record AttributeDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("physicalName")]
        public string? PhysicalName { get; init; }

        [JsonPropertyName("originalName")]
        public string? OriginalName { get; init; }

        [JsonPropertyName("dataType")]
        public string? DataType { get; init; }

        [JsonPropertyName("length")]
        public int? Length { get; init; }

        [JsonPropertyName("precision")]
        public int? Precision { get; init; }

        [JsonPropertyName("scale")]
        public int? Scale { get; init; }

        [JsonPropertyName("default")]
        public string? Default { get; init; }

        [JsonPropertyName("isMandatory")]
        public bool IsMandatory { get; init; }

        [JsonPropertyName("isIdentifier")]
        public bool IsIdentifier { get; init; }

        [JsonPropertyName("isAutoNumber")]
        public bool IsAutoNumber { get; init; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; init; } = true;

        [JsonPropertyName("isReference")]
        public int IsReference { get; init; }

        [JsonPropertyName("refEntityId")]
        public int? ReferenceEntityId { get; init; }

        [JsonPropertyName("refEntity_name")]
        public string? ReferenceEntityName { get; init; }

        [JsonPropertyName("refEntity_physicalName")]
        public string? ReferenceEntityPhysicalName { get; init; }

        [JsonPropertyName("reference_deleteRuleCode")]
        public string? ReferenceDeleteRuleCode { get; init; }

        [JsonPropertyName("reference_hasDbConstraint")]
        public int? ReferenceHasDbConstraint { get; init; }

        [JsonPropertyName("external_dbType")]
        public string? ExternalDbType { get; init; }

        [JsonPropertyName("physical_isPresentButInactive")]
        [JsonConverter(typeof(BooleanAsZeroOneConverter))]
        public int PhysicalIsPresentButInactive { get; init; }

        [JsonPropertyName("onDisk")]
        public AttributeOnDiskDocument? OnDisk { get; init; }

        [JsonPropertyName("meta")]
        [JsonConverter(typeof(AttributeMetaDescriptionConverter))]
        public AttributeMetaDocument? Meta { get; init; }

        [JsonPropertyName("reality")]
        public AttributeRealityDocument? Reality { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    internal sealed record AttributeMetaDocument
    {
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    internal sealed record AttributeOnDiskDocument
    {
        [JsonPropertyName("isNullable")]
        public bool? IsNullable { get; init; }

        [JsonPropertyName("sqlType")]
        public string? SqlType { get; init; }

        [JsonPropertyName("maxLength")]
        public int? MaxLength { get; init; }

        [JsonPropertyName("precision")]
        public int? Precision { get; init; }

        [JsonPropertyName("scale")]
        public int? Scale { get; init; }

        [JsonPropertyName("collation")]
        public string? Collation { get; init; }

        [JsonPropertyName("isIdentity")]
        public bool? IsIdentity { get; init; }

        [JsonPropertyName("isComputed")]
        public bool? IsComputed { get; init; }

        [JsonPropertyName("computedDefinition")]
        public string? ComputedDefinition { get; init; }

        [JsonPropertyName("defaultDefinition")]
        public string? DefaultDefinition { get; init; }

        [JsonPropertyName("defaultConstraint")]
        public AttributeDefaultConstraintDocument? DefaultConstraint { get; init; }

        [JsonPropertyName("checkConstraints")]
        public AttributeCheckConstraintDocument[]? CheckConstraints { get; init; }

        public AttributeOnDiskMetadata ToDomain()
        {
            var checks = CheckConstraints is null
                ? Enumerable.Empty<AttributeOnDiskCheckConstraint>()
                : CheckConstraints
                    .Select(static constraint => constraint.ToDomain())
                    .Where(static constraint => constraint is not null)
                    .Select(static constraint => constraint!);

            return AttributeOnDiskMetadata.Create(
                IsNullable,
                SqlType,
                MaxLength,
                Precision,
                Scale,
                Collation,
                IsIdentity,
                IsComputed,
                ComputedDefinition,
                DefaultDefinition,
                DefaultConstraint?.ToDomain(),
                checks);
        }
    }

    internal sealed record AttributeDefaultConstraintDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("definition")]
        public string? Definition { get; init; }

        [JsonPropertyName("isNotTrusted")]
        public bool? IsNotTrusted { get; init; }

        public AttributeOnDiskDefaultConstraint? ToDomain() => AttributeOnDiskDefaultConstraint.Create(Name, Definition, IsNotTrusted);
    }

    internal sealed record AttributeCheckConstraintDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("definition")]
        public string? Definition { get; init; }

        [JsonPropertyName("isNotTrusted")]
        public bool? IsNotTrusted { get; init; }

        public AttributeOnDiskCheckConstraint? ToDomain() => AttributeOnDiskCheckConstraint.Create(Name, Definition, IsNotTrusted);
    }
}
