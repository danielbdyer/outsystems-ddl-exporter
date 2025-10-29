using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using EntityDocument = ModelJsonDeserializer.EntityDocument;

internal sealed class EntityDocumentMapper
{
    private readonly DocumentMapperContext _context;
    private readonly AttributeDocumentMapper _attributeMapper;
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;
    private readonly IndexDocumentMapper _indexMapper;
    private readonly RelationshipDocumentMapper _relationshipMapper;
    private readonly TriggerDocumentMapper _triggerMapper;
    private readonly TemporalMetadataMapper _temporalMetadataMapper;
    private readonly SchemaResolver _schemaResolver;
    private readonly MetadataFactory _metadataFactory;
    private readonly DuplicateWarningEmitter _duplicateWarningEmitter;
    private readonly PrimaryKeyValidator _primaryKeyValidator;

    public EntityDocumentMapper(
        DocumentMapperContext context,
        AttributeDocumentMapper attributeMapper,
        ExtendedPropertyDocumentMapper extendedPropertyMapper,
        IndexDocumentMapper indexMapper,
        RelationshipDocumentMapper relationshipMapper,
        TriggerDocumentMapper triggerMapper,
        TemporalMetadataMapper temporalMetadataMapper)
    {
        _context = context;
        _attributeMapper = attributeMapper;
        _extendedPropertyMapper = extendedPropertyMapper;
        _indexMapper = indexMapper;
        _relationshipMapper = relationshipMapper;
        _triggerMapper = triggerMapper;
        _temporalMetadataMapper = temporalMetadataMapper;
        _schemaResolver = new SchemaResolver(context);
        _metadataFactory = new MetadataFactory();
        _duplicateWarningEmitter = new DuplicateWarningEmitter(context);
        _primaryKeyValidator = new PrimaryKeyValidator(context);
    }

    public Result<EntityModel> Map(ModuleName moduleName, EntityDocument doc, DocumentPathContext path)
    {
        var logicalNameResult = EntityName.Create(doc.Name);
        if (logicalNameResult.IsFailure)
        {
            return Result<EntityModel>.Failure(
                _context.WithPath(path.Property("name"), logicalNameResult.Errors));
        }

        var logicalName = logicalNameResult.Value;
        var mapContext = MapContext.Create(moduleName, logicalName, doc, path);

        var schemaResult = _schemaResolver.Resolve(mapContext);
        if (schemaResult.Result.IsFailure)
        {
            return Result<EntityModel>.Failure(schemaResult.Result.Errors);
        }

        mapContext = schemaResult.Context;
        var schema = schemaResult.Result.Value;

        var tableResult = TableName.Create(doc.PhysicalName);
        if (tableResult.IsFailure)
        {
            return Result<EntityModel>.Failure(
                _context.WithPath(path.Property("physicalName"), tableResult.Errors));
        }

        var attributesResult = _attributeMapper.Map(doc.Attributes, path.Property("attributes"));
        if (attributesResult.IsFailure)
        {
            return Result<EntityModel>.Failure(attributesResult.Errors);
        }

        var indexesResult = _indexMapper.Map(doc.Indexes, path.Property("indexes"));
        if (indexesResult.IsFailure)
        {
            return Result<EntityModel>.Failure(indexesResult.Errors);
        }

        var relationshipsResult = _relationshipMapper.Map(doc.Relationships, path.Property("relationships"));
        if (relationshipsResult.IsFailure)
        {
            return Result<EntityModel>.Failure(relationshipsResult.Errors);
        }

        var triggersResult = _triggerMapper.Map(doc.Triggers, path.Property("triggers"));
        if (triggersResult.IsFailure)
        {
            return Result<EntityModel>.Failure(triggersResult.Errors);
        }

        var temporalResult = _temporalMetadataMapper.Map(doc.Temporal, path.Property("temporal"));
        if (temporalResult.IsFailure)
        {
            return Result<EntityModel>.Failure(temporalResult.Errors);
        }

        var propertyResult = _extendedPropertyMapper.Map(
            doc.ExtendedProperties,
            path.Property("extendedProperties"));
        if (propertyResult.IsFailure)
        {
            return Result<EntityModel>.Failure(propertyResult.Errors);
        }

        var metadataResult = _metadataFactory.Create(mapContext, doc.Meta?.Description, propertyResult.Value, temporalResult.Value);
        mapContext = metadataResult.Context;
        var metadata = metadataResult.Result.Value;

        var attributes = attributesResult.Value;
        var duplicateResult = _duplicateWarningEmitter.EmitWarnings(mapContext, attributes);
        mapContext = duplicateResult.Context;

        var primaryKeyResult = _primaryKeyValidator.Validate(mapContext, attributes);
        if (primaryKeyResult.Result.IsFailure)
        {
            return Result<EntityModel>.Failure(primaryKeyResult.Result.Errors);
        }

        mapContext = primaryKeyResult.Context;
        var allowMissingPrimaryKey = primaryKeyResult.Result.Value;

        var entityResult = EntityModel.Create(
            moduleName,
            logicalName,
            tableResult.Value,
            schema,
            doc.Catalog,
            doc.IsStatic,
            doc.IsExternal,
            doc.IsActive,
            attributes,
            indexesResult.Value,
            relationshipsResult.Value,
            triggersResult.Value,
            metadata,
            allowMissingPrimaryKey: allowMissingPrimaryKey,
            allowDuplicateAttributeLogicalNames: duplicateResult.Result.Value.AllowLogicalNames,
            allowDuplicateAttributeColumnNames: duplicateResult.Result.Value.AllowColumnNames);

        if (entityResult.IsFailure)
        {
            return Result<EntityModel>.Failure(
                _context.WithPath(path, entityResult.Errors));
        }

        return entityResult;
    }

    private static ImmutableArray<ValidationError> AppendPayloadContext(
        ImmutableArray<ValidationError> errors,
        string moduleName,
        string entityName,
        string payload)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return ImmutableArray.Create(
                ValidationError.Create(
                    "entity.schema.invalid",
                    $"Entity '{moduleName}::{entityName}' has an invalid schema definition. Raw payload: {payload}"));
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(errors.Length);
        foreach (var error in errors)
        {
            builder.Add(ValidationError.Create(
                error.Code,
                $"{error.Message} (Entity '{moduleName}::{entityName}' payload: {payload})"));
        }

        return builder.ToImmutable();
    }

    internal readonly record struct MapContext(
        ModuleName ModuleName,
        EntityName LogicalName,
        EntityDocument Document,
        DocumentPathContext Path,
        string? SerializedPayload)
    {
        public static MapContext Create(ModuleName moduleName, EntityName logicalName, EntityDocument document, DocumentPathContext path)
            => new(moduleName, logicalName, document, path, null);

        public string ModuleNameValue => ModuleName.Value;

        public string EntityNameValue => LogicalName.Value;

        public DocumentPathContext SchemaPath => Path.Property("schema");

        public DocumentPathContext AttributesPath => Path.Property("attributes");

        public MapContext EnsureSerializedPayload(DocumentMapperContext context)
            => SerializedPayload is null
                ? this with { SerializedPayload = context.SerializeEntityDocument(Document) }
                : this;
    }

    internal readonly record struct HelperResult<T>(Result<T> Result, MapContext Context)
    {
        public bool IsFailure => Result.IsFailure;

        public static HelperResult<T> Success(MapContext context, T value)
            => new(Result<T>.Success(value), context);

        public static HelperResult<T> Failure(MapContext context, ValidationError error)
            => new(Result<T>.Failure(error), context);

        public static HelperResult<T> Failure(MapContext context, ImmutableArray<ValidationError> errors)
            => new(Result<T>.Failure(errors), context);
    }

    internal readonly record struct DuplicateAllowance(bool AllowLogicalNames, bool AllowColumnNames);

    internal sealed class SchemaResolver
    {
        private readonly DocumentMapperContext _context;

        public SchemaResolver(DocumentMapperContext context)
        {
            _context = context;
        }

        public HelperResult<SchemaName> Resolve(MapContext mapContext)
        {
            var options = _context.Options;
            var document = mapContext.Document;
            var moduleName = mapContext.ModuleNameValue;
            var entityName = mapContext.EntityNameValue;
            var allowMissingSchema = options.ValidationOverrides.AllowsMissingSchema(moduleName, entityName);

            if (string.IsNullOrWhiteSpace(document.Schema))
            {
                if (!allowMissingSchema)
                {
                    mapContext = mapContext.EnsureSerializedPayload(_context);
                    return HelperResult<SchemaName>.Failure(
                        mapContext,
                        _context.CreateError(
                            "entity.schema.missing",
                            $"Entity '{moduleName}::{entityName}' is missing a schema name. Raw payload: {mapContext.SerializedPayload}",
                            mapContext.SchemaPath));
                }

                var fallbackSchemaResult = options.MissingSchemaFallbackSchemaResult;
                if (fallbackSchemaResult.IsFailure)
                {
                    mapContext = mapContext.EnsureSerializedPayload(_context);
                    var errorsWithPath = _context.WithPath(mapContext.SchemaPath, fallbackSchemaResult.Errors);
                    return HelperResult<SchemaName>.Failure(
                        mapContext,
                        AppendPayloadContext(errorsWithPath, moduleName, entityName, mapContext.SerializedPayload!));
                }

                mapContext = mapContext.EnsureSerializedPayload(_context);
                _context.AddWarning(
                    $"Entity '{moduleName}::{entityName}' missing schema; using '{options.MissingSchemaFallback}'. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.SchemaPath})");
                return HelperResult<SchemaName>.Success(mapContext, fallbackSchemaResult.Value);
            }

            var schemaResult = SchemaName.Create(document.Schema);
            if (schemaResult.IsFailure)
            {
                if (!allowMissingSchema)
                {
                    mapContext = mapContext.EnsureSerializedPayload(_context);
                    var errorsWithPath = _context.WithPath(mapContext.SchemaPath, schemaResult.Errors);
                    return HelperResult<SchemaName>.Failure(
                        mapContext,
                        AppendPayloadContext(errorsWithPath, moduleName, entityName, mapContext.SerializedPayload!));
                }

                var fallbackSchemaResult = options.MissingSchemaFallbackSchemaResult;
                if (fallbackSchemaResult.IsFailure)
                {
                    mapContext = mapContext.EnsureSerializedPayload(_context);
                    var errorsWithPath = _context.WithPath(mapContext.SchemaPath, fallbackSchemaResult.Errors);
                    return HelperResult<SchemaName>.Failure(
                        mapContext,
                        AppendPayloadContext(errorsWithPath, moduleName, entityName, mapContext.SerializedPayload!));
                }

                mapContext = mapContext.EnsureSerializedPayload(_context);
                _context.AddWarning(
                    $"Entity '{moduleName}::{entityName}' schema '{document.Schema}' invalid; using '{options.MissingSchemaFallback}'. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.SchemaPath})");
                return HelperResult<SchemaName>.Success(mapContext, fallbackSchemaResult.Value);
            }

            return new HelperResult<SchemaName>(schemaResult, mapContext);
        }
    }

    internal sealed class MetadataFactory
    {
        public HelperResult<EntityMetadata> Create(
            MapContext mapContext,
            string? description,
            ImmutableArray<ExtendedProperty> extendedProperties,
            TemporalTableMetadata temporal)
        {
            var metadata = EntityMetadata.Create(description, extendedProperties, temporal);
            return HelperResult<EntityMetadata>.Success(mapContext, metadata);
        }
    }

    internal sealed class DuplicateWarningEmitter
    {
        private readonly DocumentMapperContext _context;

        public DuplicateWarningEmitter(DocumentMapperContext context)
        {
            _context = context;
        }

        public HelperResult<DuplicateAllowance> EmitWarnings(MapContext mapContext, ImmutableArray<AttributeModel> attributes)
        {
            var options = _context.Options;
            var moduleName = mapContext.ModuleNameValue;
            var entityName = mapContext.EntityNameValue;

            if (options.AllowDuplicateAttributeLogicalNames)
            {
                var duplicateLogicalGroups = attributes
                    .GroupBy(static attribute => attribute.LogicalName.Value, StringComparer.Ordinal)
                    .Where(static group => group.Count() > 1)
                    .Select(static group => new
                    {
                        Key = group.Key,
                        Columns = group.Select(static attribute => attribute.ColumnName.Value).ToArray()
                    })
                    .ToArray();

                if (duplicateLogicalGroups.Length > 0)
                {
                    mapContext = mapContext.EnsureSerializedPayload(_context);
                    foreach (var group in duplicateLogicalGroups)
                    {
                        var columnList = string.Join(", ", group.Columns.Select(static name => $"'{name}'"));
                        _context.AddWarning(
                            $"Entity '{moduleName}::{entityName}' contains duplicate attribute logical name '{group.Key}' mapped to columns {columnList}. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.AttributesPath})");
                    }
                }
            }

            if (options.AllowDuplicateAttributeColumnNames)
            {
                var duplicateColumnGroups = attributes
                    .GroupBy(static attribute => attribute.ColumnName.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(static group => group.Count() > 1)
                    .Select(static group => new
                    {
                        Key = group.Key,
                        LogicalNames = group.Select(static attribute => attribute.LogicalName.Value).ToArray()
                    })
                    .ToArray();

                if (duplicateColumnGroups.Length > 0)
                {
                    mapContext = mapContext.EnsureSerializedPayload(_context);
                    foreach (var group in duplicateColumnGroups)
                    {
                        var attributeList = string.Join(", ", group.LogicalNames.Select(static name => $"'{name}'"));
                        _context.AddWarning(
                            $"Entity '{moduleName}::{entityName}' contains duplicate attribute column name '{group.Key}' shared by attributes {attributeList}. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.AttributesPath})");
                    }
                }
            }

            var allowance = new DuplicateAllowance(
                options.AllowDuplicateAttributeLogicalNames,
                options.AllowDuplicateAttributeColumnNames);
            return HelperResult<DuplicateAllowance>.Success(mapContext, allowance);
        }
    }

    internal sealed class PrimaryKeyValidator
    {
        private readonly DocumentMapperContext _context;

        public PrimaryKeyValidator(DocumentMapperContext context)
        {
            _context = context;
        }

        public HelperResult<bool> Validate(MapContext mapContext, ImmutableArray<AttributeModel> attributes)
        {
            var moduleName = mapContext.ModuleNameValue;
            var entityName = mapContext.EntityNameValue;
            var allowMissingPrimaryKey = _context.Options.ValidationOverrides.AllowsMissingPrimaryKey(moduleName, entityName);

            if (attributes.Any(static attribute => attribute.IsIdentifier))
            {
                return HelperResult<bool>.Success(mapContext, allowMissingPrimaryKey);
            }

            mapContext = mapContext.EnsureSerializedPayload(_context);
            if (!allowMissingPrimaryKey)
            {
                return HelperResult<bool>.Failure(
                    mapContext,
                    _context.CreateError(
                        "entity.attributes.missingPrimaryKey",
                        $"Entity '{moduleName}::{entityName}' does not define a primary key attribute. Raw payload: {mapContext.SerializedPayload}",
                        mapContext.AttributesPath));
            }

            _context.AddWarning(
                $"Entity '{moduleName}::{entityName}' missing primary key; override applied. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.AttributesPath})");
            return HelperResult<bool>.Success(mapContext, allowMissingPrimaryKey);
        }
    }
}
