using System.Text.Json.Serialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
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
}
