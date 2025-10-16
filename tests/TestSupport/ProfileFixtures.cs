using System;
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
            status).Value;
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
        return ForeignKeyReality.Create(reference, hasOrphan, isNoCheck, status).Value;
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
