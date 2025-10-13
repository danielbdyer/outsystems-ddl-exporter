using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Json;

public interface IModelJsonDeserializer
{
    Result<OsmModel> Deserialize(Stream jsonStream, ICollection<string>? warnings = null);
}

public sealed partial class ModelJsonDeserializer : IModelJsonDeserializer
{
    public Result<OsmModel> Deserialize(Stream jsonStream, ICollection<string>? warnings = null)
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
                return Result<OsmModel>.Failure(ValidationError.Create("json.deserialize.failed", $"Unable to materialize CIR document: {ex.Message}"));
            }

            if (model is null)
            {
                return Result<OsmModel>.Failure(ValidationError.Create("json.document.null", "JSON document is empty."));
            }

            var modules = model.Modules ?? Array.Empty<ModuleDocument>();
            var moduleResults = new List<ModuleModel>(modules.Length);
            foreach (var module in modules)
            {
                var moduleNameResult = ModuleName.Create(module.Name);
                if (moduleNameResult.IsFailure)
                {
                    return Result<OsmModel>.Failure(moduleNameResult.Errors);
                }

                if (ShouldSkipInactiveModule(module))
                {
                    continue;
                }

                var moduleResult = MapModule(module, moduleNameResult.Value, warnings);
                if (moduleResult.IsFailure)
                {
                    return Result<OsmModel>.Failure(moduleResult.Errors);
                }

                if (moduleResult.Value is { } mappedModule)
                {
                    moduleResults.Add(mappedModule);
                }
            }

            var sequencesResult = MapSequences(model.Sequences);
            if (sequencesResult.IsFailure)
            {
                return Result<OsmModel>.Failure(sequencesResult.Errors);
            }

            var propertyResult = MapExtendedProperties(model.ExtendedProperties);
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

    private static Result<ModuleModel?> MapModule(ModuleDocument doc, ModuleName moduleName, ICollection<string>? warnings)
    {
        var entities = doc.Entities ?? Array.Empty<EntityDocument>();
        var entityResults = new List<EntityModel>(entities.Length);
        foreach (var entity in entities)
        {
            if (ShouldSkipInactiveEntity(entity))
            {
                continue;
            }

            var entityResult = MapEntity(moduleName, entity);
            if (entityResult.IsFailure)
            {
                return Result<ModuleModel?>.Failure(entityResult.Errors);
            }

            entityResults.Add(entityResult.Value);
        }

        if (entityResults.Count == 0)
        {
            warnings?.Add($"Module '{moduleName.Value}' contains no entities and will be skipped.");
            return Result<ModuleModel?>.Success(null);
        }

        var propertiesResult = MapExtendedProperties(doc.ExtendedProperties);
        if (propertiesResult.IsFailure)
        {
            return Result<ModuleModel?>.Failure(propertiesResult.Errors);
        }

        var moduleResult = ModuleModel.Create(moduleName, doc.IsSystem, doc.IsActive, entityResults, propertiesResult.Value);
        if (moduleResult.IsFailure)
        {
            return Result<ModuleModel?>.Failure(moduleResult.Errors);
        }

        return Result<ModuleModel?>.Success(moduleResult.Value);
    }

    private static Result<ImmutableArray<ExtendedProperty>> MapExtendedProperties(ExtendedPropertyDocument[]? docs)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<ExtendedProperty>>.Success(ExtendedProperty.EmptyArray);
        }

        var builder = ImmutableArray.CreateBuilder<ExtendedProperty>(docs.Length);
        foreach (var doc in docs)
        {
            if (doc is null)
            {
                continue;
            }

            var value = doc.Value.ValueKind switch
            {
                JsonValueKind.Undefined => null,
                JsonValueKind.Null => null,
                JsonValueKind.String => doc.Value.GetString(),
                _ => doc.Value.GetRawText(),
            };

            var propertyResult = ExtendedProperty.Create(doc.Name, value);
            if (propertyResult.IsFailure)
            {
                return Result<ImmutableArray<ExtendedProperty>>.Failure(propertyResult.Errors);
            }

            builder.Add(propertyResult.Value);
        }

        return Result<ImmutableArray<ExtendedProperty>>.Success(builder.ToImmutable());
    }

    private static bool ShouldSkipInactiveModule(ModuleDocument doc)
    {
        if (doc is null || doc.IsActive)
        {
            return false;
        }

        var entities = doc.Entities;
        if (entities is null || entities.Length == 0)
        {
            return true;
        }

        foreach (var entity in entities)
        {
            if (!ShouldSkipInactiveEntity(entity))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldSkipInactiveEntity(EntityDocument doc)
    {
        if (doc is null)
        {
            return false;
        }

        if (doc.IsActive)
        {
            return false;
        }

        var attributes = doc.Attributes;
        return attributes is null || attributes.Length == 0;
    }

    private static Result<EntityModel> MapEntity(ModuleName moduleName, EntityDocument doc)
    {
        var logicalNameResult = EntityName.Create(doc.Name);
        if (logicalNameResult.IsFailure)
        {
            return Result<EntityModel>.Failure(logicalNameResult.Errors);
        }

        var schemaResult = SchemaName.Create(doc.Schema);
        if (schemaResult.IsFailure)
        {
            return Result<EntityModel>.Failure(schemaResult.Errors);
        }

        var tableResult = TableName.Create(doc.PhysicalName);
        if (tableResult.IsFailure)
        {
            return Result<EntityModel>.Failure(tableResult.Errors);
        }

        var catalog = doc.Catalog;

        var attributesResult = MapAttributes(doc.Attributes);
        if (attributesResult.IsFailure)
        {
            return Result<EntityModel>.Failure(attributesResult.Errors);
        }

        var indexesResult = MapIndexes(doc.Indexes);
        if (indexesResult.IsFailure)
        {
            return Result<EntityModel>.Failure(indexesResult.Errors);
        }

        var relationshipsResult = MapRelationships(doc.Relationships);
        if (relationshipsResult.IsFailure)
        {
            return Result<EntityModel>.Failure(relationshipsResult.Errors);
        }

        var triggersResult = MapTriggers(doc.Triggers);
        if (triggersResult.IsFailure)
        {
            return Result<EntityModel>.Failure(triggersResult.Errors);
        }

        var temporalResult = MapTemporalMetadata(doc.Temporal);
        if (temporalResult.IsFailure)
        {
            return Result<EntityModel>.Failure(temporalResult.Errors);
        }

        var propertyResult = MapExtendedProperties(doc.ExtendedProperties);
        if (propertyResult.IsFailure)
        {
            return Result<EntityModel>.Failure(propertyResult.Errors);
        }

        var metadata = EntityMetadata.Create(doc.Meta?.Description, propertyResult.Value, temporalResult.Value);

        return EntityModel.Create(
            moduleName,
            logicalNameResult.Value,
            tableResult.Value,
            schemaResult.Value,
            catalog,
            doc.IsStatic,
            doc.IsExternal,
            doc.IsActive,
            attributesResult.Value,
            indexesResult.Value,
            relationshipsResult.Value,
            triggersResult.Value,
            metadata);
    }

    private static Result<ImmutableArray<AttributeModel>> MapAttributes(AttributeDocument[]? docs)
    {
        if (docs is null)
        {
            return Result<ImmutableArray<AttributeModel>>.Failure(ValidationError.Create("entity.attributes.missing", "Attributes collection is required."));
        }

        var builder = ImmutableArray.CreateBuilder<AttributeModel>(docs.Length);
        foreach (var doc in docs)
        {
            var logicalNameResult = AttributeName.Create(doc.Name);
            if (logicalNameResult.IsFailure)
            {
                return Result<ImmutableArray<AttributeModel>>.Failure(logicalNameResult.Errors);
            }

            var columnResult = ColumnName.Create(doc.PhysicalName);
            if (columnResult.IsFailure)
            {
                return Result<ImmutableArray<AttributeModel>>.Failure(columnResult.Errors);
            }

            var referenceResult = MapAttributeReference(doc);
            if (referenceResult.IsFailure)
            {
                return Result<ImmutableArray<AttributeModel>>.Failure(referenceResult.Errors);
            }

            var reality = BuildReality(doc);

            var propertyResult = MapExtendedProperties(doc.ExtendedProperties);
            if (propertyResult.IsFailure)
            {
                return Result<ImmutableArray<AttributeModel>>.Failure(propertyResult.Errors);
            }

            var metadata = AttributeMetadata.Create(doc.Meta?.Description, propertyResult.Value);
            var onDisk = doc.OnDisk?.ToDomain() ?? AttributeOnDiskMetadata.Empty;

            var attributeResult = AttributeModel.Create(
                logicalNameResult.Value,
                columnResult.Value,
            doc.DataType ?? string.Empty,
            doc.IsMandatory,
            doc.IsIdentifier,
            doc.IsAutoNumber,
            doc.IsActive,
            referenceResult.Value,
            doc.OriginalName,
            doc.Length,
            doc.Precision,
            doc.Scale,
            doc.Default,
            doc.ExternalDbType,
            reality,
            metadata,
            onDisk);

            if (attributeResult.IsFailure)
            {
                return Result<ImmutableArray<AttributeModel>>.Failure(attributeResult.Errors);
            }

            builder.Add(attributeResult.Value);
        }

        return Result<ImmutableArray<AttributeModel>>.Success(builder.ToImmutable());
    }

    private static AttributeReality BuildReality(AttributeDocument doc)
    {
        var baseReality = doc.Reality?.ToDomain() ?? AttributeReality.Unknown;
        return baseReality with { IsPresentButInactive = doc.PhysicalIsPresentButInactive == 1 };
    }

    private static Result<AttributeReference> MapAttributeReference(AttributeDocument doc)
    {
        var isReference = doc.IsReference == 1;
        EntityName? targetEntity = null;
        if (!string.IsNullOrWhiteSpace(doc.ReferenceEntityName))
        {
            var entityResult = EntityName.Create(doc.ReferenceEntityName);
            if (entityResult.IsFailure)
            {
                return Result<AttributeReference>.Failure(entityResult.Errors);
            }

            targetEntity = entityResult.Value;
        }

        TableName? targetPhysicalName = null;
        if (!string.IsNullOrWhiteSpace(doc.ReferenceEntityPhysicalName))
        {
            var tableResult = TableName.Create(doc.ReferenceEntityPhysicalName);
            if (tableResult.IsFailure)
            {
                return Result<AttributeReference>.Failure(tableResult.Errors);
            }

            targetPhysicalName = tableResult.Value;
        }

        bool? hasConstraint = doc.ReferenceHasDbConstraint switch
        {
            null => null,
            0 => false,
            _ => true
        };

        return AttributeReference.Create(
            isReference,
            doc.ReferenceEntityId,
            targetEntity,
            targetPhysicalName,
            doc.ReferenceDeleteRuleCode,
            hasConstraint);
    }

    private static Result<ImmutableArray<IndexModel>> MapIndexes(IndexDocument[]? docs)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<IndexModel>>.Success(ImmutableArray<IndexModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<IndexModel>(docs.Length);
        foreach (var doc in docs)
        {
            var nameResult = IndexName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(nameResult.Errors);
            }

            var columnResult = MapIndexColumns(doc.Columns);
            if (columnResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(columnResult.Errors);
            }

            var onDiskResult = MapIndexOnDiskMetadata(doc);
            if (onDiskResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(onDiskResult.Errors);
            }

            var propertyResult = MapExtendedProperties(doc.ExtendedProperties);
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
                return Result<ImmutableArray<IndexModel>>.Failure(indexResult.Errors);
            }

            builder.Add(indexResult.Value);
        }

        return Result<ImmutableArray<IndexModel>>.Success(builder.ToImmutable());
    }

    private static Result<ImmutableArray<SequenceModel>> MapSequences(SequenceDocument[]? docs)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<SequenceModel>>.Success(ImmutableArray<SequenceModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<SequenceModel>(docs.Length);
        foreach (var doc in docs)
        {
            if (doc is null)
            {
                continue;
            }

            var schemaResult = SchemaName.Create(doc.Schema);
            if (schemaResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(schemaResult.Errors);
            }

            var nameResult = SequenceName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(nameResult.Errors);
            }

            var propertiesResult = MapExtendedProperties(doc.ExtendedProperties);
            if (propertiesResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(propertiesResult.Errors);
            }

            var modelResult = SequenceModel.Create(
                schemaResult.Value,
                nameResult.Value,
                doc.DataType,
                doc.StartValue,
                doc.Increment,
                doc.MinValue,
                doc.MaxValue,
                doc.Cycle,
                ParseSequenceCacheMode(doc.CacheMode),
                doc.CacheSize,
                propertiesResult.Value);

            if (modelResult.IsFailure)
            {
                return Result<ImmutableArray<SequenceModel>>.Failure(modelResult.Errors);
            }

            builder.Add(modelResult.Value);
        }

        return Result<ImmutableArray<SequenceModel>>.Success(builder.ToImmutable());
    }

    private static Result<ImmutableArray<TriggerModel>> MapTriggers(TriggerDocument[]? docs)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<TriggerModel>>.Success(ImmutableArray<TriggerModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<TriggerModel>(docs.Length);
        foreach (var doc in docs)
        {
            var nameResult = TriggerName.Create(doc.Name);
            if (nameResult.IsFailure)
            {
                return Result<ImmutableArray<TriggerModel>>.Failure(nameResult.Errors);
            }

            var triggerResult = TriggerModel.Create(nameResult.Value, doc.IsDisabled, doc.Definition);
            if (triggerResult.IsFailure)
            {
                return Result<ImmutableArray<TriggerModel>>.Failure(triggerResult.Errors);
            }

            builder.Add(triggerResult.Value);
        }

        return Result<ImmutableArray<TriggerModel>>.Success(builder.ToImmutable());
    }

    private static Result<ImmutableArray<IndexColumnModel>> MapIndexColumns(IndexColumnDocument[]? docs)
    {
        if (docs is null)
        {
            return Result<ImmutableArray<IndexColumnModel>>.Failure(ValidationError.Create("index.columns.missing", "Index columns are required."));
        }

        var builder = ImmutableArray.CreateBuilder<IndexColumnModel>(docs.Length);
        foreach (var doc in docs)
        {
            var attributeResult = AttributeName.Create(doc.Attribute);
            if (attributeResult.IsFailure)
            {
                return Result<ImmutableArray<IndexColumnModel>>.Failure(attributeResult.Errors);
            }

            var columnResult = ColumnName.Create(doc.PhysicalColumn);
            if (columnResult.IsFailure)
            {
                return Result<ImmutableArray<IndexColumnModel>>.Failure(columnResult.Errors);
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
                return Result<ImmutableArray<IndexColumnModel>>.Failure(columnModelResult.Errors);
            }

            builder.Add(columnModelResult.Value);
        }

        return Result<ImmutableArray<IndexColumnModel>>.Success(builder.ToImmutable());
    }

    private static Result<IndexOnDiskMetadata> MapIndexOnDiskMetadata(IndexDocument doc)
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
                return Result<IndexOnDiskMetadata>.Failure(dataSpaceResult.Errors);
            }

            dataSpace = dataSpaceResult.Value;
        }

        var partitionColumns = ImmutableArray.CreateBuilder<IndexPartitionColumn>();
        if (doc.PartitionColumns is not null)
        {
            foreach (var column in doc.PartitionColumns)
            {
                if (column is null)
                {
                    continue;
                }

                var columnNameResult = ColumnName.Create(column.Name);
                if (columnNameResult.IsFailure)
                {
                    return Result<IndexOnDiskMetadata>.Failure(columnNameResult.Errors);
                }

                var partitionColumnResult = IndexPartitionColumn.Create(columnNameResult.Value, column.Ordinal);
                if (partitionColumnResult.IsFailure)
                {
                    return Result<IndexOnDiskMetadata>.Failure(partitionColumnResult.Errors);
                }

                partitionColumns.Add(partitionColumnResult.Value);
            }
        }

        var compressionSettings = ImmutableArray.CreateBuilder<IndexPartitionCompression>();
        if (doc.DataCompression is not null)
        {
            foreach (var compression in doc.DataCompression)
            {
                if (compression is null)
                {
                    continue;
                }

                var settingResult = IndexPartitionCompression.Create(compression.Partition, compression.Compression);
                if (settingResult.IsFailure)
                {
                    return Result<IndexOnDiskMetadata>.Failure(settingResult.Errors);
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

    private static Result<TemporalTableMetadata> MapTemporalMetadata(TemporalDocument? doc)
    {
        if (doc is null)
        {
            return TemporalTableMetadata.None;
        }

        var propertiesResult = MapExtendedProperties(doc.ExtendedProperties);
        if (propertiesResult.IsFailure)
        {
            return Result<TemporalTableMetadata>.Failure(propertiesResult.Errors);
        }

        SchemaName? historySchema = null;
        TableName? historyTable = null;
        if (doc.History is not null)
        {
            if (!string.IsNullOrWhiteSpace(doc.History.Schema))
            {
                var schemaResult = SchemaName.Create(doc.History.Schema);
                if (schemaResult.IsFailure)
                {
                    return Result<TemporalTableMetadata>.Failure(schemaResult.Errors);
                }

                historySchema = schemaResult.Value;
            }

            if (!string.IsNullOrWhiteSpace(doc.History.Name))
            {
                var tableResult = TableName.Create(doc.History.Name);
                if (tableResult.IsFailure)
                {
                    return Result<TemporalTableMetadata>.Failure(tableResult.Errors);
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
                return Result<TemporalTableMetadata>.Failure(columnResult.Errors);
            }

            periodStart = columnResult.Value;
        }

        ColumnName? periodEnd = null;
        if (!string.IsNullOrWhiteSpace(doc.PeriodEndColumn))
        {
            var columnResult = ColumnName.Create(doc.PeriodEndColumn);
            if (columnResult.IsFailure)
            {
                return Result<TemporalTableMetadata>.Failure(columnResult.Errors);
            }

            periodEnd = columnResult.Value;
        }

        var retentionResult = MapTemporalRetention(doc.Retention);
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

    private static Result<TemporalRetentionPolicy> MapTemporalRetention(TemporalRetentionDocument? doc)
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

    private static SequenceCacheMode ParseSequenceCacheMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SequenceCacheMode.Unspecified;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "cache" or "cached" => SequenceCacheMode.Cache,
            "nocache" or "no-cache" or "no_cache" => SequenceCacheMode.NoCache,
            _ => SequenceCacheMode.UnsupportedYet,
        };
    }

    private static Result<ImmutableArray<RelationshipModel>> MapRelationships(RelationshipDocument[]? docs)
    {
        if (docs is null || docs.Length == 0)
        {
            return Result<ImmutableArray<RelationshipModel>>.Success(ImmutableArray<RelationshipModel>.Empty);
        }

        var builder = ImmutableArray.CreateBuilder<RelationshipModel>(docs.Length);
        foreach (var doc in docs)
        {
            var attributeResult = AttributeName.Create(doc.ViaAttributeName);
            if (attributeResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(attributeResult.Errors);
            }

            var entityResult = EntityName.Create(doc.TargetEntityName);
            if (entityResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(entityResult.Errors);
            }

            var tableResult = TableName.Create(doc.TargetEntityPhysicalName);
            if (tableResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(tableResult.Errors);
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
                return Result<ImmutableArray<RelationshipModel>>.Failure(relationshipResult.Errors);
            }

            builder.Add(relationshipResult.Value);
        }

        return Result<ImmutableArray<RelationshipModel>>.Success(builder.ToImmutable());
    }

    private static IEnumerable<RelationshipActualConstraint> MapActualConstraints(RelationshipDocument doc)
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

    private sealed record ModelDocument
    {
        [JsonPropertyName("exportedAtUtc")]
        public DateTime ExportedAtUtc { get; init; }

        [JsonPropertyName("modules")]
        public ModuleDocument[]? Modules { get; init; }

        [JsonPropertyName("sequences")]
        public SequenceDocument[]? Sequences { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    private sealed record ModuleDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("isSystem")]
        public bool IsSystem { get; init; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; init; } = true;

        [JsonPropertyName("entities")]
        public EntityDocument[]? Entities { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    private sealed record EntityDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("physicalName")]
        public string? PhysicalName { get; init; }

        [JsonPropertyName("isStatic")]
        public bool IsStatic { get; init; }

        [JsonPropertyName("isExternal")]
        public bool IsExternal { get; init; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; init; } = true;

        [JsonPropertyName("db_catalog")]
        public string? Catalog { get; init; }

        [JsonPropertyName("db_schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("attributes")]
        public AttributeDocument[]? Attributes { get; init; }

        [JsonPropertyName("indexes")]
        public IndexDocument[]? Indexes { get; init; }

        [JsonPropertyName("relationships")]
        public RelationshipDocument[]? Relationships { get; init; }

        [JsonPropertyName("triggers")]
        public TriggerDocument[]? Triggers { get; init; }

        [JsonPropertyName("meta")]
        public EntityMetaDocument? Meta { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }

        [JsonPropertyName("temporal")]
        public TemporalDocument? Temporal { get; init; }
    }

    private sealed record AttributeDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("physicalName")]
        public string? PhysicalName { get; init; }

        [JsonPropertyName("originalName")]
        public string? OriginalName { get; init; }

        [JsonPropertyName("dataType")]
        public string? DataType { get; init; }

        [JsonPropertyName("length")]
        public int? Length { get; init; }

        [JsonPropertyName("precision")]
        public int? Precision { get; init; }

        [JsonPropertyName("scale")]
        public int? Scale { get; init; }

        [JsonPropertyName("default")]
        public string? Default { get; init; }

        [JsonPropertyName("isMandatory")]
        public bool IsMandatory { get; init; }

        [JsonPropertyName("isIdentifier")]
        public bool IsIdentifier { get; init; }

        [JsonPropertyName("isAutoNumber")]
        public bool IsAutoNumber { get; init; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; init; } = true;

        [JsonPropertyName("isReference")]
        public int IsReference { get; init; }

        [JsonPropertyName("refEntityId")]
        public int? ReferenceEntityId { get; init; }

        [JsonPropertyName("refEntity_name")]
        public string? ReferenceEntityName { get; init; }

        [JsonPropertyName("refEntity_physicalName")]
        public string? ReferenceEntityPhysicalName { get; init; }

        [JsonPropertyName("reference_deleteRuleCode")]
        public string? ReferenceDeleteRuleCode { get; init; }

        [JsonPropertyName("reference_hasDbConstraint")]
        public int? ReferenceHasDbConstraint { get; init; }

        [JsonPropertyName("external_dbType")]
        public string? ExternalDbType { get; init; }

        [JsonPropertyName("physical_isPresentButInactive")]
        public int PhysicalIsPresentButInactive { get; init; }

        [JsonPropertyName("onDisk")]
        public AttributeOnDiskDocument? OnDisk { get; init; }

        [JsonPropertyName("meta")]
        public AttributeMetaDocument? Meta { get; init; }

        [JsonPropertyName("reality")]
        public AttributeRealityDocument? Reality { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    private sealed record TriggerDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("isDisabled")]
        public bool IsDisabled { get; init; }

        [JsonPropertyName("definition")]
        public string? Definition { get; init; }
    }

    private sealed record AttributeRealityDocument
    {
        [JsonPropertyName("isNullableInDatabase")]
        public bool? IsNullableInDatabase { get; init; }

        [JsonPropertyName("hasNulls")]
        public bool? HasNulls { get; init; }

        [JsonPropertyName("hasDuplicates")]
        public bool? HasDuplicates { get; init; }

        [JsonPropertyName("hasOrphans")]
        public bool? HasOrphans { get; init; }

        public AttributeReality ToDomain() => new(IsNullableInDatabase, HasNulls, HasDuplicates, HasOrphans, false);
    }

    private sealed record IndexDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("isUnique")]
        public bool IsUnique { get; init; }

        [JsonPropertyName("isPrimary")]
        public bool IsPrimary { get; init; }

        [JsonPropertyName("isPlatformAuto")]
        public int IsPlatformAuto { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("isDisabled")]
        public bool? IsDisabled { get; init; }

        [JsonPropertyName("isPadded")]
        public bool? IsPadded { get; init; }

        [JsonPropertyName("fill_factor")]
        public int? FillFactorNew { get; init; }

        [JsonPropertyName("fillFactor")]
        public int? FillFactorLegacy { get; init; }

        [JsonIgnore]
        public int? FillFactor => FillFactorNew ?? FillFactorLegacy;

        [JsonPropertyName("ignoreDupKey")]
        public bool? IgnoreDupKey { get; init; }

        [JsonPropertyName("allowRowLocks")]
        public bool? AllowRowLocks { get; init; }

        [JsonPropertyName("allowPageLocks")]
        public bool? AllowPageLocks { get; init; }

        [JsonPropertyName("noRecompute")]
        public bool? NoRecompute { get; init; }

        [JsonPropertyName("filterDefinition")]
        public string? FilterDefinition { get; init; }

        [JsonPropertyName("dataSpace")]
        public IndexDataSpaceDocument? DataSpace { get; init; }

        [JsonPropertyName("partitionColumns")]
        public IndexPartitionColumnDocument[]? PartitionColumns { get; init; }

        [JsonPropertyName("dataCompression")]
        public IndexPartitionCompressionDocument[]? DataCompression { get; init; }

        [JsonPropertyName("columns")]
        public IndexColumnDocument[]? Columns { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    private sealed record IndexColumnDocument
    {
        [JsonPropertyName("attribute")]
        public string? Attribute { get; init; }

        [JsonPropertyName("physicalColumn")]
        public string? PhysicalColumn { get; init; }

        [JsonPropertyName("ordinal")]
        public int Ordinal { get; init; }

        [JsonPropertyName("isIncluded")]
        public bool IsIncluded { get; init; }

        [JsonPropertyName("direction")]
        public string? Direction { get; init; }
    }

    private sealed record IndexDataSpaceDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }
    }

    private sealed record IndexPartitionColumnDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("ordinal")]
        public int Ordinal { get; init; }
    }

    private sealed record IndexPartitionCompressionDocument
    {
        [JsonPropertyName("partition")]
        public int Partition { get; init; }

        [JsonPropertyName("compression")]
        public string? Compression { get; init; }
    }

    private sealed record RelationshipDocument
    {
        [JsonPropertyName("viaAttributeName")]
        public string? ViaAttributeName { get; init; }

        [JsonPropertyName("toEntity_name")]
        public string? TargetEntityName { get; init; }

        [JsonPropertyName("toEntity_physicalName")]
        public string? TargetEntityPhysicalName { get; init; }

        [JsonPropertyName("deleteRuleCode")]
        public string? DeleteRuleCode { get; init; }

        [JsonPropertyName("hasDbConstraint")]
        public int? HasDbConstraint { get; init; }

        [JsonPropertyName("actualConstraints")]
        public RelationshipConstraintDocument[]? ActualConstraints { get; init; }
    }

    private sealed record EntityMetaDocument
    {
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    private sealed record AttributeMetaDocument
    {
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    private sealed record AttributeOnDiskDocument
    {
        [JsonPropertyName("isNullable")]
        public bool? IsNullable { get; init; }

        [JsonPropertyName("sqlType")]
        public string? SqlType { get; init; }

        [JsonPropertyName("maxLength")]
        public int? MaxLength { get; init; }

        [JsonPropertyName("precision")]
        public int? Precision { get; init; }

        [JsonPropertyName("scale")]
        public int? Scale { get; init; }

        [JsonPropertyName("collation")]
        public string? Collation { get; init; }

        [JsonPropertyName("isIdentity")]
        public bool? IsIdentity { get; init; }

        [JsonPropertyName("isComputed")]
        public bool? IsComputed { get; init; }

        [JsonPropertyName("computedDefinition")]
        public string? ComputedDefinition { get; init; }

        [JsonPropertyName("defaultDefinition")]
        public string? DefaultDefinition { get; init; }

        [JsonPropertyName("defaultConstraint")]
        public AttributeDefaultConstraintDocument? DefaultConstraint { get; init; }

        [JsonPropertyName("checkConstraints")]
        public AttributeCheckConstraintDocument[]? CheckConstraints { get; init; }

        public AttributeOnDiskMetadata ToDomain()
        {
            var checks = CheckConstraints is null
                ? Enumerable.Empty<AttributeOnDiskCheckConstraint>()
                : CheckConstraints
                    .Select(static constraint => constraint.ToDomain())
                    .Where(static constraint => constraint is not null)
                    .Select(static constraint => constraint!);

            return AttributeOnDiskMetadata.Create(
                IsNullable,
                SqlType,
                MaxLength,
                Precision,
                Scale,
                Collation,
                IsIdentity,
                IsComputed,
                ComputedDefinition,
                DefaultDefinition,
                DefaultConstraint?.ToDomain(),
                checks);
        }
    }

    private sealed record AttributeDefaultConstraintDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("definition")]
        public string? Definition { get; init; }

        [JsonPropertyName("isNotTrusted")]
        public bool? IsNotTrusted { get; init; }

        public AttributeOnDiskDefaultConstraint? ToDomain() => AttributeOnDiskDefaultConstraint.Create(Name, Definition, IsNotTrusted);
    }

    private sealed record AttributeCheckConstraintDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("definition")]
        public string? Definition { get; init; }

        [JsonPropertyName("isNotTrusted")]
        public bool? IsNotTrusted { get; init; }

        public AttributeOnDiskCheckConstraint? ToDomain() => AttributeOnDiskCheckConstraint.Create(Name, Definition, IsNotTrusted);
    }

    private sealed record TemporalDocument
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("historyTable")]
        public TemporalHistoryDocument? History { get; init; }

        [JsonPropertyName("periodStartColumn")]
        public string? PeriodStartColumn { get; init; }

        [JsonPropertyName("periodEndColumn")]
        public string? PeriodEndColumn { get; init; }

        [JsonPropertyName("retention")]
        public TemporalRetentionDocument? Retention { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    private sealed record TemporalHistoryDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed record TemporalRetentionDocument
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("unit")]
        public string? Unit { get; init; }

        [JsonPropertyName("value")]
        public int? Value { get; init; }
    }

    private sealed record SequenceDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("dataType")]
        public string? DataType { get; init; }

        [JsonPropertyName("startValue")]
        public decimal? StartValue { get; init; }

        [JsonPropertyName("increment")]
        public decimal? Increment { get; init; }

        [JsonPropertyName("minValue")]
        public decimal? MinValue { get; init; }

        [JsonPropertyName("maxValue")]
        public decimal? MaxValue { get; init; }

        [JsonPropertyName("cycle")]
        public bool Cycle { get; init; }

        [JsonPropertyName("cacheMode")]
        public string? CacheMode { get; init; }

        [JsonPropertyName("cacheSize")]
        public int? CacheSize { get; init; }

        [JsonPropertyName("extendedProperties")]
        public ExtendedPropertyDocument[]? ExtendedProperties { get; init; }
    }

    private sealed record ExtendedPropertyDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("value")]
        public JsonElement Value { get; init; }
    }

    private sealed record RelationshipConstraintDocument
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("referencedSchema")]
        public string? ReferencedSchema { get; init; }

        [JsonPropertyName("referencedTable")]
        public string? ReferencedTable { get; init; }

        [JsonPropertyName("onDelete")]
        public string? OnDelete { get; init; }

        [JsonPropertyName("onUpdate")]
        public string? OnUpdate { get; init; }

        [JsonPropertyName("columns")]
        public RelationshipConstraintColumnDocument[]? Columns { get; init; }
    }

    private sealed record RelationshipConstraintColumnDocument
    {
        [JsonPropertyName("ordinal")]
        public int Ordinal { get; init; }

        [JsonPropertyName("owner.physical")]
        public string? OwnerPhysical { get; init; }

        [JsonPropertyName("owner.attribute")]
        public string? OwnerAttribute { get; init; }

        [JsonPropertyName("referenced.physical")]
        public string? ReferencedPhysical { get; init; }

        [JsonPropertyName("referenced.attribute")]
        public string? ReferencedAttribute { get; init; }
    }

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(ModelDocument))]
    private sealed partial class ModelDocumentSerializerContext : JsonSerializerContext
    {
    }
}
