using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Tests.Support;

public static class ProfileFixtures
{
    public static ProfileSnapshot LoadSnapshot(string fixtureName)
    {
        using var stream = FixtureFile.OpenStream(fixtureName);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var columns = root.GetProperty("columns")
            .EnumerateArray()
            .Select(ParseColumn)
            .ToArray();

        var uniques = root.TryGetProperty("uniqueCandidates", out var uniqueElement)
            ? uniqueElement.EnumerateArray().Select(ParseUnique).ToArray()
            : Array.Empty<UniqueCandidateProfile>();

        var composite = root.TryGetProperty("compositeUniqueCandidates", out var compositeElement)
            ? compositeElement.EnumerateArray().Select(ParseCompositeUnique).ToArray()
            : Array.Empty<CompositeUniqueCandidateProfile>();

        var fks = root.GetProperty("fkReality")
            .EnumerateArray()
            .Select(ParseForeignKey)
            .ToArray();

        return ProfileSnapshot.Create(columns, uniques, composite, fks).Value;
    }

    private static ColumnProfile ParseColumn(JsonElement element)
    {
        var schema = SchemaName.Create(element.GetProperty("Schema").GetString()).Value;
        var table = TableName.Create(element.GetProperty("Table").GetString()).Value;
        var column = ColumnName.Create(element.GetProperty("Column").GetString()).Value;
        var nullable = element.GetProperty("IsNullablePhysical").GetBoolean();
        var computed = element.GetProperty("IsComputed").GetBoolean();
        var primaryKey = element.GetProperty("IsPrimaryKey").GetBoolean();
        var uniqueKey = element.GetProperty("IsUniqueKey").GetBoolean();
        var defaultDefinition = element.GetProperty("DefaultDefinition").ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty("DefaultDefinition").GetString();
        var nullCount = element.GetProperty("NullCount").GetInt64();
        var rowCount = element.GetProperty("RowCount").GetInt64();
        var status = element.TryGetProperty("NullCountStatus", out var statusElement)
            ? ParseProbeStatus(statusElement, rowCount)
            : ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, rowCount);
        var nullSample = element.TryGetProperty("NullSample", out var sampleElement)
            ? ParseNullSample(sampleElement)
            : null;

        return ColumnProfile.Create(
            schema,
            table,
            column,
            nullable,
            computed,
            primaryKey,
            uniqueKey,
            defaultDefinition,
            rowCount,
            nullCount,
            status,
            nullSample).Value;
    }

    private static UniqueCandidateProfile ParseUnique(JsonElement element)
    {
        var schema = SchemaName.Create(element.GetProperty("Schema").GetString()).Value;
        var table = TableName.Create(element.GetProperty("Table").GetString()).Value;
        var column = ColumnName.Create(element.GetProperty("Column").GetString()).Value;
        var hasDuplicate = element.GetProperty("HasDuplicate").GetBoolean();
        var status = element.TryGetProperty("ProbeStatus", out var statusElement)
            ? ParseProbeStatus(statusElement, defaultSampleSize: 0)
            : ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 0);
        return UniqueCandidateProfile.Create(schema, table, column, hasDuplicate, status).Value;
    }

    private static CompositeUniqueCandidateProfile ParseCompositeUnique(JsonElement element)
    {
        var schema = SchemaName.Create(element.GetProperty("Schema").GetString()).Value;
        var table = TableName.Create(element.GetProperty("Table").GetString()).Value;
        var columns = element.GetProperty("Columns")
            .EnumerateArray()
            .Select(c => ColumnName.Create(c.GetString()).Value)
            .ToArray();
        var hasDuplicate = element.GetProperty("HasDuplicate").GetBoolean();
        return CompositeUniqueCandidateProfile.Create(schema, table, columns, hasDuplicate).Value;
    }

    private static ForeignKeyReality ParseForeignKey(JsonElement element)
    {
        var referenceElement = element.GetProperty("Ref");
        var fromSchema = SchemaName.Create(referenceElement.GetProperty("FromSchema").GetString()).Value;
        var fromTable = TableName.Create(referenceElement.GetProperty("FromTable").GetString()).Value;
        var fromColumn = ColumnName.Create(referenceElement.GetProperty("FromColumn").GetString()).Value;
        var toSchema = SchemaName.Create(referenceElement.GetProperty("ToSchema").GetString()).Value;
        var toTable = TableName.Create(referenceElement.GetProperty("ToTable").GetString()).Value;
        var toColumn = ColumnName.Create(referenceElement.GetProperty("ToColumn").GetString()).Value;
        var hasDbConstraint = referenceElement.GetProperty("HasDbConstraint").GetBoolean();
        var reference = ForeignKeyReference.Create(
            fromSchema,
            fromTable,
            fromColumn,
            toSchema,
            toTable,
            toColumn,
            hasDbConstraint).Value;
        var hasOrphan = element.GetProperty("HasOrphan").GetBoolean();
        var isNoCheck = element.TryGetProperty("IsNoCheck", out var isNoCheckProperty) && isNoCheckProperty.GetBoolean();
        var status = element.TryGetProperty("ProbeStatus", out var statusElement)
            ? ParseProbeStatus(statusElement, defaultSampleSize: 0)
            : ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 0);
        var orphanCount = element.TryGetProperty("OrphanCount", out var countElement)
            ? countElement.GetInt64()
            : hasOrphan ? 1 : 0;
        var orphanSample = element.TryGetProperty("OrphanSample", out var sampleElement)
            ? ParseOrphanSample(sampleElement, orphanCount)
            : null;

        return ForeignKeyReality.Create(reference, hasOrphan, orphanCount, isNoCheck, status, orphanSample).Value;
    }

    private static NullRowSample? ParseNullSample(JsonElement element)
    {
        if (!element.TryGetProperty("TotalNullRows", out var totalElement))
        {
            return null;
        }

        var totalNulls = totalElement.GetInt64();
        if (totalNulls <= 0)
        {
            return null;
        }

        var primaryKeyColumns = element.TryGetProperty("PrimaryKeyColumns", out var pkElement)
            ? pkElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToImmutableArray()
            : ImmutableArray<string>.Empty;

        var rows = element.TryGetProperty("Rows", out var rowsElement)
            ? rowsElement.EnumerateArray()
                .Select(row => new NullRowIdentifier(ParseSampleValues(row.GetProperty("PrimaryKeyValues"))))
                .ToImmutableArray()
            : ImmutableArray<NullRowIdentifier>.Empty;

        return NullRowSample.Create(primaryKeyColumns, rows, totalNulls);
    }

    private static ForeignKeyOrphanSample? ParseOrphanSample(JsonElement element, long fallbackTotal)
    {
        if (!element.TryGetProperty("TotalOrphans", out var totalElement))
        {
            return fallbackTotal > 0
                ? ForeignKeyOrphanSample.Create(ImmutableArray<string>.Empty, string.Empty, ImmutableArray<ForeignKeyOrphanIdentifier>.Empty, fallbackTotal)
                : null;
        }

        var totalOrphans = totalElement.GetInt64();
        if (totalOrphans <= 0)
        {
            return null;
        }

        var foreignKeyColumn = element.TryGetProperty("ForeignKeyColumn", out var columnElement)
            ? columnElement.GetString() ?? string.Empty
            : string.Empty;

        var primaryKeyColumns = element.TryGetProperty("PrimaryKeyColumns", out var pkElement)
            ? pkElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToImmutableArray()
            : ImmutableArray<string>.Empty;

        var rows = element.TryGetProperty("Rows", out var rowsElement)
            ? rowsElement.EnumerateArray()
                .Select(row => new ForeignKeyOrphanIdentifier(
                    ParseSampleValues(row.GetProperty("PrimaryKeyValues")),
                    row.TryGetProperty("ForeignKeyValue", out var valueElement) ? ParseSampleValue(valueElement) : null))
                .ToImmutableArray()
            : ImmutableArray<ForeignKeyOrphanIdentifier>.Empty;

        return ForeignKeyOrphanSample.Create(primaryKeyColumns, foreignKeyColumn, rows, totalOrphans);
    }

    private static ImmutableArray<object?> ParseSampleValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<object?>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<object?>();
        foreach (var value in element.EnumerateArray())
        {
            builder.Add(ParseSampleValue(value));
        }

        return builder.ToImmutable();
    }

    private static object? ParseSampleValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.ToString()
        };
    }

    private static ProfilingProbeStatus ParseProbeStatus(JsonElement element, long defaultSampleSize)
    {
        var capturedAt = element.TryGetProperty("CapturedAtUtc", out var capturedProperty)
            && capturedProperty.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(capturedProperty.GetString(), out var parsedTimestamp)
            ? parsedTimestamp
            : DateTimeOffset.UnixEpoch;

        var sampleSize = element.TryGetProperty("SampleSize", out var sampleElement)
            ? sampleElement.GetInt64()
            : defaultSampleSize;

        var outcome = element.TryGetProperty("Outcome", out var outcomeElement) && outcomeElement.ValueKind == JsonValueKind.String
            && Enum.TryParse<ProfilingProbeOutcome>(outcomeElement.GetString(), true, out var parsedOutcome)
                ? parsedOutcome
                : ProfilingProbeOutcome.Succeeded;

        return new ProfilingProbeStatus(capturedAt, sampleSize, outcome);
    }
}
