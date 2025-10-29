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
    }

    public Result<EntityModel> Map(ModuleName moduleName, EntityDocument doc, DocumentPathContext path)
    {
        var logicalNameResult = EntityName.Create(doc.Name);
        if (logicalNameResult.IsFailure)
        {
            return Result<EntityModel>.Failure(
                _context.WithPath(path.Property("name"), logicalNameResult.Errors));
        }

        var moduleNameValue = moduleName.Value;
        var logicalName = logicalNameResult.Value;
        var entityNameValue = logicalName.Value;
        string? serializedPayload = null;

        SchemaName schema;
        var allowMissingSchema = _context.Options.ValidationOverrides.AllowsMissingSchema(moduleNameValue, entityNameValue);
        if (string.IsNullOrWhiteSpace(doc.Schema))
        {
            if (!allowMissingSchema)
            {
                serializedPayload ??= _context.SerializeEntityDocument(doc);
                return Result<EntityModel>.Failure(
                    _context.CreateError(
                        "entity.schema.missing",
                        $"Entity '{moduleNameValue}::{entityNameValue}' is missing a schema name. Raw payload: {serializedPayload}",
                        path.Property("schema")));
            }

            var fallbackSchemaResult = _context.Options.MissingSchemaFallbackSchemaResult;
            if (fallbackSchemaResult.IsFailure)
            {
                serializedPayload ??= _context.SerializeEntityDocument(doc);
                var errorsWithPath = _context.WithPath(path.Property("schema"), fallbackSchemaResult.Errors);
                return Result<EntityModel>.Failure(
                    AppendPayloadContext(errorsWithPath, moduleNameValue, entityNameValue, serializedPayload));
            }

            schema = fallbackSchemaResult.Value;
            serializedPayload ??= _context.SerializeEntityDocument(doc);
            _context.AddWarning(
                $"Entity '{moduleNameValue}::{entityNameValue}' missing schema; using '{_context.Options.MissingSchemaFallback}'. Raw payload: {serializedPayload} (Path: {path.Property("schema")})");
        }
        else
        {
            var schemaResult = SchemaName.Create(doc.Schema);
            if (schemaResult.IsFailure)
            {
                if (!allowMissingSchema)
                {
                    serializedPayload ??= _context.SerializeEntityDocument(doc);
                    var errorsWithPath = _context.WithPath(path.Property("schema"), schemaResult.Errors);
                    return Result<EntityModel>.Failure(
                        AppendPayloadContext(errorsWithPath, moduleNameValue, entityNameValue, serializedPayload));
                }

                var fallbackSchemaResult = _context.Options.MissingSchemaFallbackSchemaResult;
                if (fallbackSchemaResult.IsFailure)
                {
                    serializedPayload ??= _context.SerializeEntityDocument(doc);
                    var errorsWithPath = _context.WithPath(path.Property("schema"), fallbackSchemaResult.Errors);
                    return Result<EntityModel>.Failure(
                        AppendPayloadContext(errorsWithPath, moduleNameValue, entityNameValue, serializedPayload));
                }

                schema = fallbackSchemaResult.Value;
                serializedPayload ??= _context.SerializeEntityDocument(doc);
                _context.AddWarning(
                    $"Entity '{moduleNameValue}::{entityNameValue}' schema '{doc.Schema}' invalid; using '{_context.Options.MissingSchemaFallback}'. Raw payload: {serializedPayload} (Path: {path.Property("schema")})");
            }
            else
            {
                schema = schemaResult.Value;
            }
        }

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

        var metadata = EntityMetadata.Create(doc.Meta?.Description, propertyResult.Value, temporalResult.Value);
        var attributes = attributesResult.Value;
        var attributesPath = path.Property("attributes");

        var allowDuplicateLogicalNames = _context.Options.AllowDuplicateAttributeLogicalNames;
        if (allowDuplicateLogicalNames)
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
                serializedPayload ??= _context.SerializeEntityDocument(doc);
                foreach (var group in duplicateLogicalGroups)
                {
                    var columnList = string.Join(", ", group.Columns.Select(static name => $"'{name}'"));
                    _context.AddWarning(
                        $"Entity '{moduleNameValue}::{entityNameValue}' contains duplicate attribute logical name '{group.Key}' mapped to columns {columnList}. Raw payload: {serializedPayload} (Path: {attributesPath})");
                }
            }
        }

        var allowDuplicateColumnNames = _context.Options.AllowDuplicateAttributeColumnNames;
        if (allowDuplicateColumnNames)
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
                serializedPayload ??= _context.SerializeEntityDocument(doc);
                foreach (var group in duplicateColumnGroups)
                {
                    var attributeList = string.Join(", ", group.LogicalNames.Select(static name => $"'{name}'"));
                    _context.AddWarning(
                        $"Entity '{moduleNameValue}::{entityNameValue}' contains duplicate attribute column name '{group.Key}' shared by attributes {attributeList}. Raw payload: {serializedPayload} (Path: {attributesPath})");
                }
            }
        }

        var allowMissingPrimaryKey = _context.Options.ValidationOverrides.AllowsMissingPrimaryKey(moduleNameValue, entityNameValue);
        if (!attributes.Any(static a => a.IsIdentifier))
        {
            serializedPayload ??= _context.SerializeEntityDocument(doc);
            if (!allowMissingPrimaryKey)
            {
                return Result<EntityModel>.Failure(
                    _context.CreateError(
                        "entity.attributes.missingPrimaryKey",
                        $"Entity '{moduleNameValue}::{entityNameValue}' does not define a primary key attribute. Raw payload: {serializedPayload}",
                        path.Property("attributes")));
            }

            _context.AddWarning(
                $"Entity '{moduleNameValue}::{entityNameValue}' missing primary key; override applied. Raw payload: {serializedPayload} (Path: {path.Property("attributes")})");
        }

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
            allowDuplicateAttributeLogicalNames: allowDuplicateLogicalNames,
            allowDuplicateAttributeColumnNames: allowDuplicateColumnNames);

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
}
