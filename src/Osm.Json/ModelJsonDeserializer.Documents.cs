using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Model;

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
        [JsonConverter(typeof(EntityMetaDocumentConverter))]
        public EntityMetaDocument? Meta { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }

        [JsonPropertyName("temporal")]
        public TemporalDocument? Temporal { get; init; }
    }

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
        [JsonConverter(typeof(BooleanAsZeroOneIntConverter))]
        public int PhysicalIsPresentButInactive { get; init; }

        [JsonPropertyName("onDisk")]
        public AttributeOnDiskDocument? OnDisk { get; init; }

        [JsonPropertyName("meta")]
        [JsonConverter(typeof(AttributeMetaDocumentConverter))]
        public AttributeMetaDocument? Meta { get; init; }

        [JsonPropertyName("reality")]
        public AttributeRealityDocument? Reality { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
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

    internal sealed record IndexDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("isUnique")]
        public bool IsUnique { get; init; }

        [JsonPropertyName("isPrimary")]
        public bool IsPrimary { get; init; }

        [JsonPropertyName("isPlatformAuto")]
        public int IsPlatformAuto { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("isDisabled")]
        public bool? IsDisabled { get; init; }

        [JsonPropertyName("isPadded")]
        public bool? IsPadded { get; init; }

        [JsonPropertyName("fill_factor")]
        public int? FillFactorNew { get; init; }

        [JsonPropertyName("fillFactor")]
        public int? FillFactorLegacy { get; init; }

        [JsonIgnore]
        public int? FillFactor => FillFactorNew ?? FillFactorLegacy;

        [JsonPropertyName("ignoreDupKey")]
        public bool? IgnoreDupKey { get; init; }

        [JsonPropertyName("allowRowLocks")]
        public bool? AllowRowLocks { get; init; }

        [JsonPropertyName("allowPageLocks")]
        public bool? AllowPageLocks { get; init; }

        [JsonPropertyName("noRecompute")]
        public bool? NoRecompute { get; init; }

        [JsonPropertyName("filterDefinition")]
        public string? FilterDefinition { get; init; }

        [JsonPropertyName("dataSpace")]
        public IndexDataSpaceDocument? DataSpace { get; init; }

        [JsonPropertyName("partitionColumns")]
        public IndexPartitionColumnDocument[]? PartitionColumns { get; init; }

        [JsonPropertyName("dataCompression")]
        public IndexPartitionCompressionDocument[]? DataCompression { get; init; }

        [JsonPropertyName("columns")]
        public IndexColumnDocument[]? Columns { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    internal sealed record IndexColumnDocument
    {
        [JsonPropertyName("attribute")]
        public string? Attribute { get; init; }

        [JsonPropertyName("physicalColumn")]
        public string? PhysicalColumn { get; init; }

        [JsonPropertyName("ordinal")]
        public int Ordinal { get; init; }

        [JsonPropertyName("isIncluded")]
        public bool IsIncluded { get; init; }

        [JsonPropertyName("direction")]
        public string? Direction { get; init; }
    }

    internal sealed record IndexDataSpaceDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }
    }

    internal sealed record IndexPartitionColumnDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("ordinal")]
        public int Ordinal { get; init; }
    }

    internal sealed record IndexPartitionCompressionDocument
    {
        [JsonPropertyName("partition")]
        public int Partition { get; init; }

        [JsonPropertyName("compression")]
        public string? Compression { get; init; }
    }

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

    internal sealed record EntityMetaDocument
    {
        [JsonPropertyName("description")]
        public string? Description { get; init; }
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

    internal sealed record SequenceDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("dataType")]
        public string? DataType { get; init; }

        [JsonPropertyName("startValue")]
        public decimal? StartValue { get; init; }

        [JsonPropertyName("increment")]
        public decimal? Increment { get; init; }

        [JsonPropertyName("minValue")]
        public decimal? MinValue { get; init; }

        [JsonPropertyName("maxValue")]
        public decimal? MaxValue { get; init; }

        [JsonPropertyName("cycle")]
        public bool Cycle { get; init; }

        [JsonPropertyName("cacheMode")]
        public string? CacheMode { get; init; }

        [JsonPropertyName("cacheSize")]
        public int? CacheSize { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    internal sealed record ExtendedPropertyDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("value")]
        public JsonElement Value { get; init; }
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

    private sealed class EntityMetaDocumentConverter : JsonConverter<EntityMetaDocument?>
    {
        public override EntityMetaDocument? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => new EntityMetaDocument { Description = NormalizeDescription(reader.GetString()) },
                JsonTokenType.StartObject => ReadObject(ref reader),
                _ => throw new JsonException($"Unsupported token '{reader.TokenType}' for entity meta.")
            };
        }

        public override void Write(Utf8JsonWriter writer, EntityMetaDocument? value, JsonSerializerOptions options)
        {
            if (value is null || string.IsNullOrWhiteSpace(value.Description))
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.Description);
        }

        private static EntityMetaDocument? ReadObject(ref Utf8JsonReader reader)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Entity meta must be an object with a description property.");
            }

            if (element.TryGetProperty("description", out var descriptionElement))
            {
                if (descriptionElement.ValueKind is JsonValueKind.Null)
                {
                    return new EntityMetaDocument { Description = null };
                }

                return new EntityMetaDocument { Description = NormalizeDescription(descriptionElement.GetString()) };
            }

            return new EntityMetaDocument { Description = null };
        }

        private static string? NormalizeDescription(string? description)
            => string.IsNullOrWhiteSpace(description) ? null : description;
    }

    private sealed class AttributeMetaDocumentConverter : JsonConverter<AttributeMetaDocument?>
    {
        public override AttributeMetaDocument? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => new AttributeMetaDocument { Description = NormalizeDescription(reader.GetString()) },
                JsonTokenType.StartObject => ReadObject(ref reader),
                _ => throw new JsonException($"Unsupported token '{reader.TokenType}' for attribute meta.")
            };
        }

        public override void Write(Utf8JsonWriter writer, AttributeMetaDocument? value, JsonSerializerOptions options)
        {
            if (value is null || string.IsNullOrWhiteSpace(value.Description))
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.Description);
        }

        private static AttributeMetaDocument? ReadObject(ref Utf8JsonReader reader)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Attribute meta must be an object with a description property.");
            }

            if (element.TryGetProperty("description", out var descriptionElement))
            {
                if (descriptionElement.ValueKind is JsonValueKind.Null)
                {
                    return new AttributeMetaDocument { Description = null };
                }

                return new AttributeMetaDocument { Description = NormalizeDescription(descriptionElement.GetString()) };
            }

            return new AttributeMetaDocument { Description = null };
        }

        private static string? NormalizeDescription(string? description)
            => string.IsNullOrWhiteSpace(description) ? null : description;
    }

    private sealed class BooleanAsZeroOneIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number when reader.TryGetInt32(out var numericValue):
                    if (numericValue is 0 or 1)
                    {
                        return numericValue;
                    }

                    break;
                case JsonTokenType.True:
                    return 1;
                case JsonTokenType.False:
                    return 0;
                case JsonTokenType.String:
                    var text = reader.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        break;
                    }

                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is 0 or 1)
                    {
                        return parsed;
                    }

                    if (bool.TryParse(text, out var boolValue))
                    {
                        return boolValue ? 1 : 0;
                    }

                    break;
            }

            throw new JsonException("Expected boolean or 0/1 value.");
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(ModelDocument))]
    private sealed partial class ModelDocumentSerializerContext : JsonSerializerContext
    {
    }
}
