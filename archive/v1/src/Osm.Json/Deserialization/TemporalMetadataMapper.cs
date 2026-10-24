using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using TemporalDocument = ModelJsonDeserializer.TemporalDocument;
using TemporalRetentionDocument = ModelJsonDeserializer.TemporalRetentionDocument;

internal sealed class TemporalMetadataMapper
{
    private readonly DocumentMapperContext _context;
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;

    public TemporalMetadataMapper(
        DocumentMapperContext context,
        ExtendedPropertyDocumentMapper extendedPropertyMapper)
    {
        _context = context;
        _extendedPropertyMapper = extendedPropertyMapper;
    }

    public Result<TemporalTableMetadata> Map(TemporalDocument? doc, DocumentPathContext path)
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

        var retentionResult = MapRetention(doc.Retention, path.Property("retention"));
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

    private Result<TemporalRetentionPolicy?> MapRetention(TemporalRetentionDocument? doc, DocumentPathContext path)
    {
        if (doc is null)
        {
            return Result<TemporalRetentionPolicy?>.Success(null);
        }

        var policy = TemporalRetentionPolicy.Create(
            ParseTemporalRetentionKind(doc.Kind),
            doc.Value,
            ParseTemporalRetentionUnit(doc.Unit));

        return Result<TemporalRetentionPolicy?>.Success(policy);
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
}
