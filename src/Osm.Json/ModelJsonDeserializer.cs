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

        ModelDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(jsonStream, ModelDocumentSerializerContext.Default.ModelDocument);
        }
        catch (JsonException ex)
        {
            return Result<OsmModel>.Failure(ValidationError.Create("json.parse.failed", $"Invalid JSON payload: {ex.Message}"));
        }

        if (document is null)
        {
            return Result<OsmModel>.Failure(ValidationError.Create("json.document.null", "JSON document is empty."));
        }

        var modules = document.Modules ?? Array.Empty<ModuleDocument>();
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

        return OsmModel.Create(document.ExportedAtUtc, moduleResults);
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

        var moduleResult = ModuleModel.Create(moduleName, doc.IsSystem, doc.IsActive, entityResults);
        if (moduleResult.IsFailure)
        {
            return Result<ModuleModel?>.Failure(moduleResult.Errors);
        }

        return Result<ModuleModel?>.Success(moduleResult.Value);
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

        var metadata = EntityMetadata.Create(doc.Meta?.Description);

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

        var metadata = AttributeMetadata.Create(doc.Meta?.Description);
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

            var isPrimary = doc.IsPrimary || onDiskResult.Value.Kind == IndexKind.PrimaryKey;
            var indexResult = IndexModel.Create(
                nameResult.Value,
                doc.IsUnique,
                isPrimary,
                doc.IsPlatformAuto != 0,
                columnResult.Value,
                onDiskResult.Value);

            if (indexResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(indexResult.Errors);
            }

            builder.Add(indexResult.Value);
        }

        return Result<ImmutableArray<IndexModel>>.Success(builder.ToImmutable());
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

        [JsonPropertyName("fillFactor")]
        public int? FillFactor { get; init; }

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
