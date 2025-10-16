using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using EntityDocument = ModelJsonDeserializer.EntityDocument;
using IndexColumnDocument = ModelJsonDeserializer.IndexColumnDocument;
using IndexDataSpaceDocument = ModelJsonDeserializer.IndexDataSpaceDocument;
using IndexDocument = ModelJsonDeserializer.IndexDocument;
using IndexPartitionColumnDocument = ModelJsonDeserializer.IndexPartitionColumnDocument;
using IndexPartitionCompressionDocument = ModelJsonDeserializer.IndexPartitionCompressionDocument;
using RelationshipConstraintColumnDocument = ModelJsonDeserializer.RelationshipConstraintColumnDocument;
using RelationshipConstraintDocument = ModelJsonDeserializer.RelationshipConstraintDocument;
using RelationshipDocument = ModelJsonDeserializer.RelationshipDocument;
using TemporalDocument = ModelJsonDeserializer.TemporalDocument;
using TemporalRetentionDocument = ModelJsonDeserializer.TemporalRetentionDocument;
using TriggerDocument = ModelJsonDeserializer.TriggerDocument;

internal sealed class EntityDocumentMapper
{
    private readonly DocumentMapperContext _context;
    private readonly AttributeDocumentMapper _attributeMapper;
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;

    public EntityDocumentMapper(
        DocumentMapperContext context,
        AttributeDocumentMapper attributeMapper,
        ExtendedPropertyDocumentMapper extendedPropertyMapper)
    {
        _context = context;
        _attributeMapper = attributeMapper;
        _extendedPropertyMapper = extendedPropertyMapper;
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

            var fallbackSchemaResult = _context.Options.MissingSchemaFallbackSchema;
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

                var fallbackSchemaResult = _context.Options.MissingSchemaFallbackSchema;
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

        var indexesResult = MapIndexes(doc.Indexes, path.Property("indexes"));
        if (indexesResult.IsFailure)
        {
            return Result<EntityModel>.Failure(indexesResult.Errors);
        }

        var relationshipsResult = MapRelationships(doc.Relationships, path.Property("relationships"));
        if (relationshipsResult.IsFailure)
        {
            return Result<EntityModel>.Failure(relationshipsResult.Errors);
        }

        var triggersResult = MapTriggers(doc.Triggers, path.Property("triggers"));
        if (triggersResult.IsFailure)
        {
            return Result<EntityModel>.Failure(triggersResult.Errors);
        }

        var temporalResult = MapTemporalMetadata(doc.Temporal, path.Property("temporal"));
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
            allowMissingPrimaryKey: allowMissingPrimaryKey);

        if (entityResult.IsFailure)
        {
            return Result<EntityModel>.Failure(
                _context.WithPath(path, entityResult.Errors));
        }

        return entityResult;
    }

    private Result<ImmutableArray<IndexModel>> MapIndexes(IndexDocument[]? docs, DocumentPathContext path)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<IndexModel>>.Success(ImmutableArray<IndexModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<IndexModel>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc is null)
            {
                continue;
            }

            var indexPath = path.Index(i);
            var nameResult = IndexName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(
                    _context.WithPath(indexPath.Property("name"), nameResult.Errors));
            }

            var columnResult = MapIndexColumns(doc.Columns, indexPath.Property("columns"));
            if (columnResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(columnResult.Errors);
            }

            var onDiskResult = MapIndexOnDiskMetadata(doc, indexPath);
            if (onDiskResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(onDiskResult.Errors);
            }

            var propertyResult = _extendedPropertyMapper.Map(
                doc.ExtendedProperties,
                indexPath.Property("extendedProperties"));
            if (propertyResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(propertyResult.Errors);
            }

            var isPrimary = doc.IsPrimary || onDiskResult.Value.Kind == IndexKind.PrimaryKey;
            var indexResult = IndexModel.Create(
                nameResult.Value,
                doc.IsUnique,
                isPrimary,
                doc.IsPlatformAuto != 0,
                columnResult.Value,
                onDiskResult.Value,
                propertyResult.Value);

            if (indexResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(
                    _context.WithPath(indexPath, indexResult.Errors));
            }

            builder.Add(indexResult.Value);
        }

        return Result<ImmutableArray<IndexModel>>.Success(builder.ToImmutable());
    }

    private Result<ImmutableArray<IndexColumnModel>> MapIndexColumns(IndexColumnDocument[]? docs, DocumentPathContext path)
    {
        if (docs is null)
        {
            return Result<ImmutableArray<IndexColumnModel>>.Failure(
                _context.CreateError("index.columns.missing", "Index columns are required.", path));
        }

        var builder = ImmutableArray.CreateBuilder<IndexColumnModel>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            var columnPath = path.Index(i);

            var attributeResult = AttributeName.Create(doc.Attribute);
            if (attributeResult.IsFailure)
            {
                return Result<ImmutableArray<IndexColumnModel>>.Failure(
                    _context.WithPath(columnPath.Property("attribute"), attributeResult.Errors));
            }

            var columnResult = ColumnName.Create(doc.PhysicalColumn);
            if (columnResult.IsFailure)
            {
                return Result<ImmutableArray<IndexColumnModel>>.Failure(
                    _context.WithPath(columnPath.Property("physicalColumn"), columnResult.Errors));
            }

            var direction = ParseIndexDirection(doc.Direction);
            var columnModelResult = IndexColumnModel.Create(
                attributeResult.Value,
                columnResult.Value,
                doc.Ordinal,
                doc.IsIncluded,
                direction);
            if (columnModelResult.IsFailure)
            {
                return Result<ImmutableArray<IndexColumnModel>>.Failure(
                    _context.WithPath(columnPath, columnModelResult.Errors));
            }

            builder.Add(columnModelResult.Value);
        }

        return Result<ImmutableArray<IndexColumnModel>>.Success(builder.ToImmutable());
    }

    private Result<IndexOnDiskMetadata> MapIndexOnDiskMetadata(IndexDocument doc, DocumentPathContext path)
    {
        var kind = ParseIndexKind(doc.Kind);
        IndexDataSpace? dataSpace = null;
        if (doc.DataSpace is not null &&
            !string.IsNullOrWhiteSpace(doc.DataSpace.Name) &&
            !string.IsNullOrWhiteSpace(doc.DataSpace.Type))
        {
            var dataSpaceResult = IndexDataSpace.Create(doc.DataSpace.Name, doc.DataSpace.Type);
            if (dataSpaceResult.IsFailure)
            {
                return Result<IndexOnDiskMetadata>.Failure(
                    _context.WithPath(path.Property("dataSpace"), dataSpaceResult.Errors));
            }

            dataSpace = dataSpaceResult.Value;
        }

        var partitionColumns = ImmutableArray.CreateBuilder<IndexPartitionColumn>();
        if (doc.PartitionColumns is not null)
        {
            for (var i = 0; i < doc.PartitionColumns.Length; i++)
            {
                var column = doc.PartitionColumns[i];
                if (column is null)
                {
                    continue;
                }

                var columnPath = path.Property("partitionColumns").Index(i);
                var columnNameResult = ColumnName.Create(column.Name);
                if (columnNameResult.IsFailure)
                {
                    return Result<IndexOnDiskMetadata>.Failure(
                        _context.WithPath(columnPath.Property("name"), columnNameResult.Errors));
                }

                var partitionColumnResult = IndexPartitionColumn.Create(columnNameResult.Value, column.Ordinal);
                if (partitionColumnResult.IsFailure)
                {
                    return Result<IndexOnDiskMetadata>.Failure(
                        _context.WithPath(columnPath, partitionColumnResult.Errors));
                }

                partitionColumns.Add(partitionColumnResult.Value);
            }
        }

        var compressionSettings = ImmutableArray.CreateBuilder<IndexPartitionCompression>();
        if (doc.DataCompression is not null)
        {
            for (var i = 0; i < doc.DataCompression.Length; i++)
            {
                var compression = doc.DataCompression[i];
                if (compression is null)
                {
                    continue;
                }

                var compressionPath = path.Property("dataCompression").Index(i);
                var settingResult = IndexPartitionCompression.Create(compression.Partition, compression.Compression);
                if (settingResult.IsFailure)
                {
                    return Result<IndexOnDiskMetadata>.Failure(
                        _context.WithPath(compressionPath, settingResult.Errors));
                }

                compressionSettings.Add(settingResult.Value);
            }
        }

        var metadata = IndexOnDiskMetadata.Create(
            kind,
            doc.IsDisabled ?? false,
            doc.IsPadded ?? false,
            doc.FillFactor,
            doc.IgnoreDupKey ?? false,
            doc.AllowRowLocks ?? true,
            doc.AllowPageLocks ?? true,
            doc.NoRecompute ?? false,
            doc.FilterDefinition,
            dataSpace,
            partitionColumns.ToImmutable(),
            compressionSettings.ToImmutable());

        return Result<IndexOnDiskMetadata>.Success(metadata);
    }

    private Result<ImmutableArray<TriggerModel>> MapTriggers(TriggerDocument[]? docs, DocumentPathContext path)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<TriggerModel>>.Success(ImmutableArray<TriggerModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<TriggerModel>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc is null)
            {
                continue;
            }

            var triggerPath = path.Index(i);
            var nameResult = TriggerName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<ImmutableArray<TriggerModel>>.Failure(
                    _context.WithPath(triggerPath.Property("name"), nameResult.Errors));
            }

            var triggerResult = TriggerModel.Create(nameResult.Value, doc.IsDisabled, doc.Definition);
            if (triggerResult.IsFailure)
            {
                return Result<ImmutableArray<TriggerModel>>.Failure(
                    _context.WithPath(triggerPath, triggerResult.Errors));
            }

            builder.Add(triggerResult.Value);
        }

        return Result<ImmutableArray<TriggerModel>>.Success(builder.ToImmutable());
    }

    private Result<TemporalTableMetadata> MapTemporalMetadata(TemporalDocument? doc, DocumentPathContext path)
    {
        if (doc is null)
        {
            return TemporalTableMetadata.None;
        }

        var propertiesResult = _extendedPropertyMapper.Map(
            doc.ExtendedProperties,
            path.Property("extendedProperties"));
        if (propertiesResult.IsFailure)
        {
            return Result<TemporalTableMetadata>.Failure(propertiesResult.Errors);
        }

        SchemaName? historySchema = null;
        TableName? historyTable = null;
        if (doc.History is not null)
        {
            var historyPath = path.Property("historyTable");
            if (!string.IsNullOrWhiteSpace(doc.History.Schema))
            {
                var schemaResult = SchemaName.Create(doc.History.Schema);
                if (schemaResult.IsFailure)
                {
                    return Result<TemporalTableMetadata>.Failure(
                        _context.WithPath(historyPath.Property("schema"), schemaResult.Errors));
                }

                historySchema = schemaResult.Value;
            }

            if (!string.IsNullOrWhiteSpace(doc.History.Name))
            {
                var tableResult = TableName.Create(doc.History.Name);
                if (tableResult.IsFailure)
                {
                    return Result<TemporalTableMetadata>.Failure(
                        _context.WithPath(historyPath.Property("name"), tableResult.Errors));
                }

                historyTable = tableResult.Value;
            }
        }

        ColumnName? periodStart = null;
        if (!string.IsNullOrWhiteSpace(doc.PeriodStartColumn))
        {
            var columnResult = ColumnName.Create(doc.PeriodStartColumn);
            if (columnResult.IsFailure)
            {
                return Result<TemporalTableMetadata>.Failure(
                    _context.WithPath(path.Property("periodStartColumn"), columnResult.Errors));
            }

            periodStart = columnResult.Value;
        }

        ColumnName? periodEnd = null;
        if (!string.IsNullOrWhiteSpace(doc.PeriodEndColumn))
        {
            var columnResult = ColumnName.Create(doc.PeriodEndColumn);
            if (columnResult.IsFailure)
            {
                return Result<TemporalTableMetadata>.Failure(
                    _context.WithPath(path.Property("periodEndColumn"), columnResult.Errors));
            }

            periodEnd = columnResult.Value;
        }

        var retentionResult = MapTemporalRetention(doc.Retention, path.Property("retention"));
        if (retentionResult.IsFailure)
        {
            return Result<TemporalTableMetadata>.Failure(retentionResult.Errors);
        }

        var metadata = TemporalTableMetadata.Create(
            ParseTemporalType(doc.Type),
            historySchema,
            historyTable,
            periodStart,
            periodEnd,
            retentionResult.Value,
            propertiesResult.Value);

        return Result<TemporalTableMetadata>.Success(metadata);
    }

    private Result<TemporalRetentionPolicy> MapTemporalRetention(TemporalRetentionDocument? doc, DocumentPathContext path)
    {
        if (doc is null)
        {
            return TemporalRetentionPolicy.None;
        }

        var kind = ParseTemporalRetentionKind(doc.Kind);
        var unit = ParseTemporalRetentionUnit(doc.Unit);
        var policy = TemporalRetentionPolicy.Create(kind, doc.Value, unit);
        return Result<TemporalRetentionPolicy>.Success(policy);
    }

    private Result<ImmutableArray<RelationshipModel>> MapRelationships(RelationshipDocument[]? docs, DocumentPathContext path)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<RelationshipModel>>.Success(ImmutableArray<RelationshipModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<RelationshipModel>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc is null)
            {
                continue;
            }

            var relationshipPath = path.Index(i);
            var attributeResult = AttributeName.Create(doc.ViaAttributeName);
            if (attributeResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(
                    _context.WithPath(relationshipPath.Property("viaAttributeName"), attributeResult.Errors));
            }

            var entityResult = EntityName.Create(doc.TargetEntityName);
            if (entityResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(
                    _context.WithPath(relationshipPath.Property("toEntity_name"), entityResult.Errors));
            }

            var tableResult = TableName.Create(doc.TargetEntityPhysicalName);
            if (tableResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(
                    _context.WithPath(relationshipPath.Property("toEntity_physicalName"), tableResult.Errors));
            }

            var hasConstraint = doc.HasDbConstraint switch
            {
                null => (bool?)null,
                0 => false,
                _ => true
            };

            var relationshipResult = RelationshipModel.Create(
                attributeResult.Value,
                entityResult.Value,
                tableResult.Value,
                doc.DeleteRuleCode,
                hasConstraint,
                MapActualConstraints(doc));

            if (relationshipResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(
                    _context.WithPath(relationshipPath, relationshipResult.Errors));
            }

            builder.Add(relationshipResult.Value);
        }

        return Result<ImmutableArray<RelationshipModel>>.Success(builder.ToImmutable());
    }

    private IEnumerable<RelationshipActualConstraint> MapActualConstraints(RelationshipDocument doc)
    {
        if (doc.ActualConstraints is null || doc.ActualConstraints.Length == 0)
        {
            return Array.Empty<RelationshipActualConstraint>();
        }

        var constraints = new List<RelationshipActualConstraint>(doc.ActualConstraints.Length);
        foreach (var constraint in doc.ActualConstraints)
        {
            var columns = constraint.Columns is null || constraint.Columns.Length == 0
                ? ImmutableArray<RelationshipActualConstraintColumn>.Empty
                : constraint.Columns
                    .Select(c => RelationshipActualConstraintColumn.Create(
                        c.OwnerPhysical,
                        c.OwnerAttribute,
                        c.ReferencedPhysical,
                        c.ReferencedAttribute,
                        c.Ordinal))
                    .ToImmutableArray();

            constraints.Add(RelationshipActualConstraint.Create(
                constraint.Name ?? string.Empty,
                constraint.ReferencedSchema,
                constraint.ReferencedTable,
                constraint.OnDelete,
                constraint.OnUpdate,
                columns));
        }

        return constraints;
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

    private static IndexKind ParseIndexKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return IndexKind.Unknown;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "PK" => IndexKind.PrimaryKey,
            "UQ" => IndexKind.UniqueConstraint,
            "UIX" => IndexKind.UniqueIndex,
            "IX" => IndexKind.NonUniqueIndex,
            "CLUSTERED" or "CL" => IndexKind.ClusteredIndex,
            "NONCLUSTERED" or "NON-CLUSTERED" or "NC" => IndexKind.NonClusteredIndex,
            _ => IndexKind.Unknown,
        };
    }

    private static TemporalTableType ParseTemporalType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TemporalTableType.None;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "systemversioned" or "system-versioned" or "system_versioned" => TemporalTableType.SystemVersioned,
            "history" or "historytable" or "history_table" => TemporalTableType.HistoryTable,
            _ => TemporalTableType.UnsupportedYet,
        };
    }

    private static TemporalRetentionKind ParseTemporalRetentionKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TemporalRetentionKind.None;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "none" => TemporalRetentionKind.None,
            "infinite" => TemporalRetentionKind.Infinite,
            "limited" => TemporalRetentionKind.Limited,
            _ => TemporalRetentionKind.UnsupportedYet,
        };
    }

    private static TemporalRetentionUnit ParseTemporalRetentionUnit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TemporalRetentionUnit.None;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "day" or "days" => TemporalRetentionUnit.Days,
            "week" or "weeks" => TemporalRetentionUnit.Weeks,
            "month" or "months" => TemporalRetentionUnit.Months,
            "year" or "years" => TemporalRetentionUnit.Years,
            _ => TemporalRetentionUnit.UnsupportedYet,
        };
    }

    private static IndexColumnDirection ParseIndexDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return IndexColumnDirection.Unspecified;
        }

        var normalized = direction.Trim();
        if (string.Equals(normalized, "DESC", StringComparison.OrdinalIgnoreCase))
        {
            return IndexColumnDirection.Descending;
        }

        if (string.Equals(normalized, "ASC", StringComparison.OrdinalIgnoreCase))
        {
            return IndexColumnDirection.Ascending;
        }

        return IndexColumnDirection.Unspecified;
    }
}
