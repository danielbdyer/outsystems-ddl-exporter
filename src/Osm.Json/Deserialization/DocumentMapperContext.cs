using System;
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
        PayloadSerializerOptions = payloadSerializerOptions ?? throw new ArgumentNullException(nameof(payloadSerializerOptions));
        Reset(options, warnings);
    }

    public ModelJsonDeserializerOptions Options { get; private set; } = ModelJsonDeserializerOptions.Default;

    public ICollection<string>? Warnings { get; private set; }

    public JsonSerializerOptions PayloadSerializerOptions { get; }

    public void Reset(ModelJsonDeserializerOptions options, ICollection<string>? warnings)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Warnings = warnings;
    }

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

    /// <summary>
    /// Maps an optional document array into an immutable model array, applying the
    /// shared lenient skeleton: a null/empty source yields an empty array, null
    /// elements are skipped, each surviving element is mapped with its indexed
    /// path, and the first element failure short-circuits the whole array.
    /// </summary>
    public Result<ImmutableArray<TModel>> MapArray<TDoc, TModel>(
        TDoc[]? docs,
        DocumentPathContext path,
        Func<TDoc, DocumentPathContext, Result<TModel>> mapElement)
        where TDoc : class
    {
        if (mapElement is null)
        {
            throw new ArgumentNullException(nameof(mapElement));
        }

        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<TModel>>.Success(ImmutableArray<TModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<TModel>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc is null)
            {
                continue;
            }

            var result = mapElement(doc, path.Index(i));
            if (result.IsFailure)
            {
                return Result<ImmutableArray<TModel>>.Failure(result.Errors);
            }

            builder.Add(result.Value);
        }

        return Result<ImmutableArray<TModel>>.Success(builder.ToImmutable());
    }

    public ImmutableArray<ValidationError> WithPath(DocumentPathContext path, ImmutableArray<ValidationError> errors)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return errors;
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(errors.Length);
        foreach (var error in errors)
        {
            builder.Add(AppendPath(error, path));
        }

        return builder.ToImmutable();
    }

    public ValidationError CreateError(string code, string message, DocumentPathContext path)
        => AppendPath(ValidationError.Create(code, message), path);

    private static ValidationError AppendPath(ValidationError error, DocumentPathContext path)
        => error
            .WithMessage(AppendPathMessage(error.Message, path))
            .WithMetadata("json.path", path.ToString());

    private static string AppendPathMessage(string message, DocumentPathContext path)
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
