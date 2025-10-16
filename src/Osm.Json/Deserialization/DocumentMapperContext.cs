using System.Collections.Generic;
using System.Text.Json;
using Osm.Json;

namespace Osm.Json.Deserialization;

internal sealed class DocumentMapperContext
{
    public DocumentMapperContext(
        ModelJsonDeserializerOptions options,
        ICollection<string>? warnings,
        JsonSerializerOptions payloadSerializerOptions)
    {
        Options = options;
        Warnings = warnings;
        PayloadSerializerOptions = payloadSerializerOptions;
    }

    public ModelJsonDeserializerOptions Options { get; }

    public ICollection<string>? Warnings { get; }

    public JsonSerializerOptions PayloadSerializerOptions { get; }

    public void AddWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Warnings?.Add(message);
    }

    public string SerializeEntityDocument(ModelJsonDeserializer.EntityDocument doc)
        => JsonSerializer.Serialize(doc, PayloadSerializerOptions);
}
