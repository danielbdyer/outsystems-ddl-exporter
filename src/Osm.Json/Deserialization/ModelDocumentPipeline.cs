using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Json.Deserialization;

using ModuleDocument = ModelJsonDeserializer.ModuleDocument;

internal sealed class ModelDocumentPipeline
{
    private readonly IModelDocumentSchemaValidator _schemaValidator;
    private readonly DocumentMapperContext _context;
    private readonly ModuleDocumentMapper _moduleMapper;
    private readonly SequenceDocumentMapper _sequenceMapper;
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;

    public ModelDocumentPipeline(
        JsonSerializerOptions payloadSerializerOptions,
        IModelDocumentSchemaValidator schemaValidator,
        IModelDocumentMapperFactory mapperFactory)
    {
        if (payloadSerializerOptions is null)
        {
            throw new ArgumentNullException(nameof(payloadSerializerOptions));
        }

        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        if (mapperFactory is null)
        {
            throw new ArgumentNullException(nameof(mapperFactory));
        }

        _context = new DocumentMapperContext(ModelJsonDeserializerOptions.Default, null, payloadSerializerOptions);
        var mappers = mapperFactory.Create(_context);
        _moduleMapper = mappers.ModuleMapper;
        _sequenceMapper = mappers.SequenceMapper;
        _extendedPropertyMapper = mappers.ExtendedPropertyMapper;
    }

    public Result<ModelDocumentPipelineResult> Process(
        JsonElement root,
        ModelJsonDeserializer.ModelDocument model,
        ModelJsonDeserializerOptions options,
        ICollection<string>? warnings)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _context.Reset(options, warnings);

        var schemaResult = _schemaValidator.Validate(root);
        if (schemaResult.IsFailure)
        {
            AppendSchemaWarnings(warnings, schemaResult.Errors);
        }

        var rootPath = DocumentPathContext.Root;
        var modulesResult = MapModules(model.Modules, rootPath);
        if (modulesResult.IsFailure)
        {
            return Result<ModelDocumentPipelineResult>.Failure(modulesResult.Errors);
        }

        var sequencesResult = _sequenceMapper.Map(model.Sequences, rootPath.Property("sequences"));
        if (sequencesResult.IsFailure)
        {
            return Result<ModelDocumentPipelineResult>.Failure(sequencesResult.Errors);
        }

        var propertiesResult = _extendedPropertyMapper.Map(
            model.ExtendedProperties,
            rootPath.Property("extendedProperties"));
        if (propertiesResult.IsFailure)
        {
            return Result<ModelDocumentPipelineResult>.Failure(propertiesResult.Errors);
        }

        return Result<ModelDocumentPipelineResult>.Success(
            new ModelDocumentPipelineResult(
                schemaResult.IsSuccess,
                modulesResult.Value,
                sequencesResult.Value,
                propertiesResult.Value));
    }

    private Result<ImmutableArray<ModuleModel>> MapModules(ModuleDocument[]? modules, DocumentPathContext rootPath)
    {
        if (modules is null || modules.Length == 0)
        {
            return Result<ImmutableArray<ModuleModel>>.Success(ImmutableArray<ModuleModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<ModuleModel>(modules.Length);
        for (var moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
        {
            var modulePath = rootPath.Property("modules").Index(moduleIndex);
            var module = modules[moduleIndex];
            if (module is null)
            {
                return Result<ImmutableArray<ModuleModel>>.Failure(
                    _context.CreateError(
                        "json.module.null",
                        "Modules array cannot contain null entries.",
                        modulePath));
            }

            var moduleNameResult = ModuleName.Create(module.Name);
            if (moduleNameResult.IsFailure)
            {
                return Result<ImmutableArray<ModuleModel>>.Failure(
                    _context.WithPath(modulePath.Property("name"), moduleNameResult.Errors));
            }

            if (_moduleMapper.ShouldSkipInactiveModule(module))
            {
                continue;
            }

            var moduleResult = _moduleMapper.Map(module, moduleNameResult.Value, modulePath);
            if (moduleResult.IsFailure)
            {
                return Result<ImmutableArray<ModuleModel>>.Failure(moduleResult.Errors);
            }

            if (moduleResult.Value is { } mappedModule)
            {
                builder.Add(mappedModule);
            }
        }

        return Result<ImmutableArray<ModuleModel>>.Success(builder.ToImmutable());
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

internal sealed record ModelDocumentPipelineResult(
    bool SchemaIsValid,
    ImmutableArray<ModuleModel> Modules,
    ImmutableArray<SequenceModel> Sequences,
    ImmutableArray<ExtendedProperty> ExtendedProperties);
