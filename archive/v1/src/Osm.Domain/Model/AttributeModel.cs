using Osm.Domain.ValueObjects;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Model;

public sealed record AttributeModel(
    AttributeName LogicalName,
    ColumnName ColumnName,
    string? OriginalName,
    string DataType,
    int? Length,
    int? Precision,
    int? Scale,
    string? DefaultValue,
    bool IsMandatory,
    bool IsIdentifier,
    bool IsAutoNumber,
    bool IsActive,
    AttributeReference Reference,
    string? ExternalDatabaseType,
    AttributeReality Reality,
    AttributeMetadata Metadata,
    AttributeOnDiskMetadata OnDisk)
{
    public static Result<AttributeModel> Create(
        AttributeName logicalName,
        ColumnName columnName,
        string dataType,
        bool isMandatory,
        bool isIdentifier,
        bool isAutoNumber,
        bool isActive,
        AttributeReference? reference = null,
        string? originalName = null,
        int? length = null,
        int? precision = null,
        int? scale = null,
        string? defaultValue = null,
        string? externalDatabaseType = null,
        AttributeReality? reality = null,
        AttributeMetadata? metadata = null,
        AttributeOnDiskMetadata? onDisk = null)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return Result<AttributeModel>.Failure(ValidationError.Create("attribute.dataType.invalid", "Data type must be provided."));
        }

        if (length is < 0)
        {
            return Result<AttributeModel>.Failure(ValidationError.Create("attribute.length.invalid", "Length must be non-negative."));
        }

        if (precision is < 0)
        {
            return Result<AttributeModel>.Failure(ValidationError.Create("attribute.precision.invalid", "Precision must be non-negative."));
        }

        if (scale is < 0)
        {
            return Result<AttributeModel>.Failure(ValidationError.Create("attribute.scale.invalid", "Scale must be non-negative."));
        }

        var referenceResult = reference ?? AttributeReference.None;
        var trimmedDataType = dataType.Trim();
        var trimmedOriginal = string.IsNullOrWhiteSpace(originalName) ? null : originalName!.Trim();
        var trimmedDefault = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue;
        var trimmedExternal = string.IsNullOrWhiteSpace(externalDatabaseType) ? null : externalDatabaseType!.Trim();

        return Result<AttributeModel>.Success(new AttributeModel(
            logicalName,
            columnName,
            trimmedOriginal,
            trimmedDataType,
            length,
            precision,
            scale,
            trimmedDefault,
            isMandatory,
            isIdentifier,
            isAutoNumber,
            isActive,
            referenceResult,
            trimmedExternal,
            reality ?? AttributeReality.Unknown,
            metadata ?? AttributeMetadata.Empty,
            onDisk ?? AttributeOnDiskMetadata.Empty));
    }
}
