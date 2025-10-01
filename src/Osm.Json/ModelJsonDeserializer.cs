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
    Result<OsmModel> Deserialize(Stream jsonStream);
}

public sealed class ModelJsonDeserializer : IModelJsonDeserializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Result<OsmModel> Deserialize(Stream jsonStream)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        ModelDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ModelDocument>(jsonStream, SerializerOptions);
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
            var moduleResult = MapModule(module);
            if (moduleResult.IsFailure)
            {
                return Result<OsmModel>.Failure(moduleResult.Errors);
            }

            moduleResults.Add(moduleResult.Value);
        }

        return OsmModel.Create(document.ExportedAtUtc, moduleResults);
    }

    private static Result<ModuleModel> MapModule(ModuleDocument doc)
    {
        var moduleNameResult = ModuleName.Create(doc.Name);
        if (moduleNameResult.IsFailure)
        {
            return Result<ModuleModel>.Failure(moduleNameResult.Errors);
        }

        var entities = doc.Entities ?? Array.Empty<EntityDocument>();
        var entityResults = new List<EntityModel>(entities.Length);
        foreach (var entity in entities)
        {
            var entityResult = MapEntity(moduleNameResult.Value, entity);
            if (entityResult.IsFailure)
            {
                return Result<ModuleModel>.Failure(entityResult.Errors);
            }

            entityResults.Add(entityResult.Value);
        }

        return ModuleModel.Create(moduleNameResult.Value, doc.IsSystem, doc.IsActive, entityResults);
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
            relationshipsResult.Value);
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
                reality);

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

            var indexResult = IndexModel.Create(
                nameResult.Value,
                doc.IsUnique,
                doc.IsPrimary,
                doc.IsPlatformAuto != 0,
                columnResult.Value);

            if (indexResult.IsFailure)
            {
                return Result<ImmutableArray<IndexModel>>.Failure(indexResult.Errors);
            }

            builder.Add(indexResult.Value);
        }

        return Result<ImmutableArray<IndexModel>>.Success(builder.ToImmutable());
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

            var columnModelResult = IndexColumnModel.Create(attributeResult.Value, columnResult.Value, doc.Ordinal);
            if (columnModelResult.IsFailure)
            {
                return Result<ImmutableArray<IndexColumnModel>>.Failure(columnModelResult.Errors);
            }

            builder.Add(columnModelResult.Value);
        }

        return Result<ImmutableArray<IndexColumnModel>>.Success(builder.ToImmutable());
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
                hasConstraint);

            if (relationshipResult.IsFailure)
            {
                return Result<ImmutableArray<RelationshipModel>>.Failure(relationshipResult.Errors);
            }

            builder.Add(relationshipResult.Value);
        }

        return Result<ImmutableArray<RelationshipModel>>.Success(builder.ToImmutable());
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

        [JsonPropertyName("reality")]
        public AttributeRealityDocument? Reality { get; init; }
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
    }
}
