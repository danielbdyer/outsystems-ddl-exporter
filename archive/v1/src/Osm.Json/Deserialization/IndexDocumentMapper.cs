using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using IndexColumnDocument = ModelJsonDeserializer.IndexColumnDocument;
using IndexDataSpaceDocument = ModelJsonDeserializer.IndexDataSpaceDocument;
using IndexDocument = ModelJsonDeserializer.IndexDocument;
using IndexPartitionColumnDocument = ModelJsonDeserializer.IndexPartitionColumnDocument;
using IndexPartitionCompressionDocument = ModelJsonDeserializer.IndexPartitionCompressionDocument;

internal sealed class IndexDocumentMapper
{
    private readonly DocumentMapperContext _context;
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;

    public IndexDocumentMapper(
        DocumentMapperContext context,
        ExtendedPropertyDocumentMapper extendedPropertyMapper)
    {
        _context = context;
        _extendedPropertyMapper = extendedPropertyMapper;
    }

    public Result<ImmutableArray<IndexModel>> Map(IndexDocument[]? docs, DocumentPathContext path)
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
