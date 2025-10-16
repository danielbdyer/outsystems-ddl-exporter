using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Json;

namespace Osm.Json.Deserialization;

internal sealed class DocumentMapperContext
{
    private const string PathSuffixFormat = " (Path: {0})";

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

    public ImmutableArray<ValidationError> WithPath(DocumentPathContext path, ImmutableArray<ValidationError> errors)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return errors;
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(errors.Length);
        foreach (var error in errors)
        {
            builder.Add(new ValidationError(error.Code, AppendPath(error.Message, path)));
        }

        return builder.ToImmutable();
    }

    public ValidationError CreateError(string code, string message, DocumentPathContext path)
        => ValidationError.Create(code, AppendPath(message, path));

    private static string AppendPath(string message, DocumentPathContext path)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return $"Unknown validation failure{string.Format(PathSuffixFormat, path)}";
        }

        return message.EndsWith('.')
            ? message[..^1] + string.Format(PathSuffixFormat, path) + "."
            : message + string.Format(PathSuffixFormat, path);
    }
}
