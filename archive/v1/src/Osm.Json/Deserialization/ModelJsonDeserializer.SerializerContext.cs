using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Json.Deserialization;

namespace Osm.Json;

public sealed partial class ModelJsonDeserializer
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = ModelDocumentSerializerContext.PayloadSerializerOptions;

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        GenerationMode = JsonSourceGenerationMode.Metadata,
        Converters = new[]
        {
            typeof(EntityMetaDescriptionConverter),
            typeof(AttributeMetaDescriptionConverter)
        })]
    [JsonSerializable(typeof(ModelDocument))]
    [JsonSerializable(typeof(ModuleDocument))]
    [JsonSerializable(typeof(EntityDocument))]
    [JsonSerializable(typeof(AttributeDocument))]
    [JsonSerializable(typeof(AttributeOnDiskDocument))]
    [JsonSerializable(typeof(AttributeDefaultConstraintDocument))]
    [JsonSerializable(typeof(AttributeCheckConstraintDocument))]
    [JsonSerializable(typeof(AttributeRealityDocument))]
    [JsonSerializable(typeof(IndexDocument))]
    [JsonSerializable(typeof(IndexColumnDocument))]
    [JsonSerializable(typeof(IndexDataSpaceDocument))]
    [JsonSerializable(typeof(IndexPartitionColumnDocument))]
    [JsonSerializable(typeof(IndexPartitionCompressionDocument))]
    [JsonSerializable(typeof(RelationshipDocument))]
    [JsonSerializable(typeof(RelationshipConstraintDocument))]
    [JsonSerializable(typeof(RelationshipConstraintColumnDocument))]
    [JsonSerializable(typeof(TriggerDocument))]
    [JsonSerializable(typeof(TemporalDocument))]
    [JsonSerializable(typeof(TemporalHistoryDocument))]
    [JsonSerializable(typeof(TemporalRetentionDocument))]
    [JsonSerializable(typeof(SequenceDocument))]
    [JsonSerializable(typeof(ExtendedPropertyDocument))]
    [JsonSerializable(typeof(EntityMetaDocument))]
    [JsonSerializable(typeof(AttributeMetaDocument))]
    private sealed partial class ModelDocumentSerializerContext : JsonSerializerContext
    {
        public static JsonSerializerOptions PayloadSerializerOptions { get; } = CreatePayloadSerializerOptions();

        private static JsonSerializerOptions CreatePayloadSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            options.Converters.Add(new EntityMetaDescriptionConverter());
            options.Converters.Add(new AttributeMetaDescriptionConverter());

            return options;
        }
    }
}
