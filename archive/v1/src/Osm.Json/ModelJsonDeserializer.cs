using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json.Deserialization;

namespace Osm.Json;

public interface IModelJsonDeserializer
{
    Result<OsmModel> Deserialize(
        Stream jsonStream,
        ICollection<string>? warnings = null,
        ModelJsonDeserializerOptions? options = null);
}

public sealed class ModelJsonDeserializerOptions
{
    public ModelJsonDeserializerOptions(
        ModuleValidationOverrides? validationOverrides = null,
        string? missingSchemaFallback = null,
        bool allowDuplicateAttributeLogicalNames = false,
        bool allowDuplicateAttributeColumnNames = false)
    {
        ValidationOverrides = validationOverrides ?? ModuleValidationOverrides.Empty;
        MissingSchemaFallback = string.IsNullOrWhiteSpace(missingSchemaFallback)
            ? "dbo"
            : missingSchemaFallback.Trim();
        AllowDuplicateAttributeLogicalNames = allowDuplicateAttributeLogicalNames;
        AllowDuplicateAttributeColumnNames = allowDuplicateAttributeColumnNames;

        var fallbackResult = SchemaName.Create(MissingSchemaFallback);
        MissingSchemaFallbackSchemaResult = fallbackResult.IsSuccess
            ? fallbackResult
            : Result<SchemaName>.Failure(fallbackResult.Errors.Select(error => ValidationError.Create(
                error.Code,
                $"Invalid missing schema fallback '{MissingSchemaFallback}': {error.Message}")));
    }

    public ModuleValidationOverrides ValidationOverrides { get; }

    public string MissingSchemaFallback { get; }

    public Result<SchemaName> MissingSchemaFallbackSchemaResult { get; }

    public SchemaName? MissingSchemaFallbackSchema => MissingSchemaFallbackSchemaResult.IsSuccess
        ? MissingSchemaFallbackSchemaResult.Value
        : null;

    public bool AllowDuplicateAttributeLogicalNames { get; }

    public bool AllowDuplicateAttributeColumnNames { get; }

    public ModelJsonDeserializerOptions WithAllowDuplicateAttributeLogicalNames(bool allow)
        => AllowDuplicateAttributeLogicalNames == allow
            ? this
            : new ModelJsonDeserializerOptions(
                ValidationOverrides,
                MissingSchemaFallback,
                allow,
                AllowDuplicateAttributeColumnNames);

    public ModelJsonDeserializerOptions WithAllowDuplicateAttributeColumnNames(bool allow)
        => AllowDuplicateAttributeColumnNames == allow
            ? this
            : new ModelJsonDeserializerOptions(
                ValidationOverrides,
                MissingSchemaFallback,
                AllowDuplicateAttributeLogicalNames,
                allow);

    public static ModelJsonDeserializerOptions Default { get; } = new();
}

public sealed partial class ModelJsonDeserializer : IModelJsonDeserializer
{
    private static readonly Lazy<ModelDocumentPipeline> SharedPipeline = new(
        () => new ModelDocumentPipeline(
            PayloadSerializerOptions,
            new CirSchemaValidatorAdapter(),
            new ModelDocumentMapperFactory()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ModelDocumentPipeline _pipeline;

    public ModelJsonDeserializer()
        : this(SharedPipeline.Value)
    {
    }

    internal ModelJsonDeserializer(ModelDocumentPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Result<OsmModel> Deserialize(Stream jsonStream, ICollection<string>? warnings = null, ModelJsonDeserializerOptions? options = null)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonStream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            var error = ValidationError
                .Create("json.parse.failed", $"Invalid JSON payload: {ex.Message}")
                .WithMetadata("json.path", string.IsNullOrWhiteSpace(ex.Path) ? "$" : ex.Path);
            return Result<OsmModel>.Failure(error);
        }

        using (document)
        {
            options ??= ModelJsonDeserializerOptions.Default;

            ModelDocument? model;
            try
            {
                model = document.RootElement.Deserialize(ModelDocumentSerializerContext.Default.ModelDocument);
            }
            catch (JsonException ex)
            {
                var path = string.IsNullOrWhiteSpace(ex.Path) ? "$" : ex.Path!;
                var location = ex.LineNumber is { } line && ex.BytePositionInLine is { } position
                    ? $" (line {line + 1}, byte {position})"
                    : string.Empty;
                var innerMessage = ex.InnerException?.Message;
                var details = innerMessage is null ? ex.Message : $"{ex.Message} ({innerMessage})";

                return Result<OsmModel>.Failure(
                    ValidationError
                        .Create(
                            "json.deserialize.failed",
                            $"Unable to materialize CIR document at {path}{location}: {details}")
                        .WithMetadata("json.path", path));
            }

            if (model is null)
            {
                return Result<OsmModel>.Failure(ValidationError.Create("json.document.null", "JSON document is empty."));
            }

            var pipelineResult = _pipeline.Process(
                document.RootElement,
                model,
                options,
                warnings);

            if (pipelineResult.IsFailure)
            {
                return Result<OsmModel>.Failure(AttachJsonPathMetadata(pipelineResult.Errors));
            }

            var value = pipelineResult.Value;
            return OsmModel.Create(
                model.ExportedAtUtc,
                value.Modules,
                value.Sequences,
                value.ExtendedProperties);
        }
    }

    private static ImmutableArray<ValidationError> AttachJsonPathMetadata(ImmutableArray<ValidationError> errors)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return errors;
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(errors.Length);
        foreach (var error in errors)
        {
            if (error.Metadata.ContainsKey("json.path"))
            {
                builder.Add(error);
                continue;
            }

            if (TryExtractPath(error.Message, out var path))
            {
                builder.Add(error.WithMetadata("json.path", path));
            }
            else
            {
                builder.Add(error);
            }
        }

        return builder.ToImmutable();
    }

    private static bool TryExtractPath(string message, out string path)
    {
        const string token = "(Path: ";
        var start = message.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
        {
            path = string.Empty;
            return false;
        }

        start += token.Length;
        var end = message.IndexOf(')', start);
        if (end < 0)
        {
            path = string.Empty;
            return false;
        }

        path = message[start..end];
        return true;
    }
}
