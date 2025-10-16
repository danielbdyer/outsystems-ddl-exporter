using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        string? missingSchemaFallback = null)
    {
        ValidationOverrides = validationOverrides ?? ModuleValidationOverrides.Empty;
        MissingSchemaFallback = string.IsNullOrWhiteSpace(missingSchemaFallback)
            ? "dbo"
            : missingSchemaFallback.Trim();
    }

    public ModuleValidationOverrides ValidationOverrides { get; }

    public string MissingSchemaFallback { get; }

    public static ModelJsonDeserializerOptions Default { get; } = new();
}

public sealed partial class ModelJsonDeserializer : IModelJsonDeserializer
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
            return Result<OsmModel>.Failure(ValidationError.Create("json.parse.failed", $"Invalid JSON payload: {ex.Message}"));
        }

        using (document)
        {
            options ??= ModelJsonDeserializerOptions.Default;

            var schemaResult = CirSchemaValidator.Validate(document.RootElement);
            if (schemaResult.IsFailure)
            {
                AppendSchemaWarnings(warnings, schemaResult.Errors);
            }

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
                    ValidationError.Create(
                        "json.deserialize.failed",
                        $"Unable to materialize CIR document at {path}{location}: {details}"));
            }

            if (model is null)
            {
                return Result<OsmModel>.Failure(ValidationError.Create("json.document.null", "JSON document is empty."));
            }

            var mapperContext = new DocumentMapperContext(options, warnings, PayloadSerializerOptions);
            var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(mapperContext);
            var attributeMapper = new AttributeDocumentMapper(mapperContext, extendedPropertyMapper);
            var entityMapper = new EntityDocumentMapper(mapperContext, attributeMapper, extendedPropertyMapper);
            var moduleMapper = new ModuleDocumentMapper(mapperContext, entityMapper, extendedPropertyMapper);
            var sequenceMapper = new SequenceDocumentMapper(mapperContext, extendedPropertyMapper);
            var rootPath = DocumentPathContext.Root;

            var modules = model.Modules ?? Array.Empty<ModuleDocument>();
            var moduleResults = new List<ModuleModel>(modules.Length);
            for (var moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
            {
                var module = modules[moduleIndex];
                var modulePath = rootPath.Property("modules").Index(moduleIndex);
                var moduleNameResult = ModuleName.Create(module.Name);
                if (moduleNameResult.IsFailure)
                {
                    return Result<OsmModel>.Failure(
                        mapperContext.WithPath(modulePath.Property("name"), moduleNameResult.Errors));
                }

                if (moduleMapper.ShouldSkipInactiveModule(module))
                {
                    continue;
                }

                var moduleResult = moduleMapper.Map(module, moduleNameResult.Value, modulePath);
                if (moduleResult.IsFailure)
                {
                    return Result<OsmModel>.Failure(moduleResult.Errors);
                }

                if (moduleResult.Value is { } mappedModule)
                {
                    moduleResults.Add(mappedModule);
                }
            }

            var sequencesResult = sequenceMapper.Map(model.Sequences, rootPath.Property("sequences"));
            if (sequencesResult.IsFailure)
            {
                return Result<OsmModel>.Failure(sequencesResult.Errors);
            }

            var propertyResult = extendedPropertyMapper.Map(
                model.ExtendedProperties,
                rootPath.Property("extendedProperties"));
            if (propertyResult.IsFailure)
            {
                return Result<OsmModel>.Failure(propertyResult.Errors);
            }

            return OsmModel.Create(model.ExportedAtUtc, moduleResults, sequencesResult.Value, propertyResult.Value);
        }
    }

    private static void AppendSchemaWarnings(ICollection<string>? warnings, ImmutableArray<ValidationError> errors)
    {
        if (warnings is null || errors.IsDefaultOrEmpty)
        {
            return;
        }

        var totalIssues = errors.Length;
        warnings.Add($"Schema validation encountered {totalIssues} issue(s). Proceeding with best-effort import.");

        var sampleCount = Math.Min(3, totalIssues);
        for (var i = 0; i < sampleCount; i++)
        {
            warnings.Add($"  Example {i + 1}: {errors[i].Message}");
        }

        if (totalIssues > sampleCount)
        {
            warnings.Add($"  … {totalIssues - sampleCount} additional issue(s) suppressed.");
        }
    }
}
