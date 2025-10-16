using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using AttributeDocument = ModelJsonDeserializer.AttributeDocument;

internal sealed class AttributeDocumentMapper
{
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;

    public AttributeDocumentMapper(ExtendedPropertyDocumentMapper extendedPropertyMapper)
    {
        _extendedPropertyMapper = extendedPropertyMapper;
    }

    public Result<ImmutableArray<AttributeModel>> Map(AttributeDocument[]? docs)
    {
        if (docs is null)
        {
            return Result<ImmutableArray<AttributeModel>>.Failure(
                ValidationError.Create("entity.attributes.missing", "Attributes collection is required."));
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

            var propertyResult = _extendedPropertyMapper.Map(doc.ExtendedProperties);
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
}
