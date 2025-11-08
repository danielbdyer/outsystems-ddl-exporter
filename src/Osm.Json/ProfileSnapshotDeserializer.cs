using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Json;

public interface IProfileSnapshotDeserializer
{
    Result<ProfileSnapshot> Deserialize(Stream jsonStream);
}

public sealed class ProfileSnapshotDeserializer : IProfileSnapshotDeserializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Result<ProfileSnapshot> Deserialize(Stream jsonStream)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        ProfileSnapshotDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProfileSnapshotDocument>(jsonStream, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create("profile.json.parseFailed", $"Invalid profiling JSON payload: {ex.Message}"));
        }

        if (document is null)
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create("profile.json.empty", "Profiling JSON document is empty."));
        }

        if (document.Columns is null)
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create("profile.columns.missing", "Profiling JSON must include a 'columns' array."));
        }

        var columnResults = Result.Collect(document.Columns.Select(MapColumn));
        if (columnResults.IsFailure)
        {
            return Result<ProfileSnapshot>.Failure(columnResults.Errors);
        }

        var uniqueResults = Result.Collect((document.UniqueCandidates ?? Array.Empty<UniqueCandidateDocument>()).Select(MapUniqueCandidate));
        if (uniqueResults.IsFailure)
        {
            return Result<ProfileSnapshot>.Failure(uniqueResults.Errors);
        }

        var compositeResults = Result.Collect((document.CompositeUniqueCandidates ?? Array.Empty<CompositeUniqueCandidateDocument>()).Select(MapCompositeUnique));
        if (compositeResults.IsFailure)
        {
            return Result<ProfileSnapshot>.Failure(compositeResults.Errors);
        }

        var foreignKeyResults = Result.Collect((document.ForeignKeys ?? Array.Empty<ForeignKeyDocument>()).Select(MapForeignKey));
        if (foreignKeyResults.IsFailure)
        {
            return Result<ProfileSnapshot>.Failure(foreignKeyResults.Errors);
        }

        return ProfileSnapshot.Create(columnResults.Value, uniqueResults.Value, compositeResults.Value, foreignKeyResults.Value);
    }

    private static Result<ColumnProfile> MapColumn(ColumnDocument doc)
    {
        var schemaResult = SchemaName.Create(doc.Schema);
        if (schemaResult.IsFailure)
        {
            return Result<ColumnProfile>.Failure(
                DecorateCoordinateMetadata(schemaResult.Errors, doc.Schema, doc.Table, doc.Column));
        }

        var tableResult = TableName.Create(doc.Table);
        if (tableResult.IsFailure)
        {
            return Result<ColumnProfile>.Failure(
                DecorateCoordinateMetadata(tableResult.Errors, doc.Schema, doc.Table, doc.Column));
        }

        var columnResult = ColumnName.Create(doc.Column);
        if (columnResult.IsFailure)
        {
            return Result<ColumnProfile>.Failure(
                DecorateCoordinateMetadata(columnResult.Errors, doc.Schema, doc.Table, doc.Column));
        }

        var status = MapProbeStatus(doc.NullCountStatus, doc.RowCount);
        var nullSample = MapNullSample(doc.NullSample);

        return ColumnProfile.Create(
            schemaResult.Value,
            tableResult.Value,
            columnResult.Value,
            doc.IsNullablePhysical,
            doc.IsComputed,
            doc.IsPrimaryKey,
            doc.IsUniqueKey,
            doc.DefaultDefinition,
            doc.RowCount,
            doc.NullCount,
            status,
            nullSample);
    }

    private static Result<UniqueCandidateProfile> MapUniqueCandidate(UniqueCandidateDocument doc)
    {
        var schemaResult = SchemaName.Create(doc.Schema);
        if (schemaResult.IsFailure)
        {
            return Result<UniqueCandidateProfile>.Failure(
                DecorateCoordinateMetadata(schemaResult.Errors, doc.Schema, doc.Table, doc.Column));
        }

        var tableResult = TableName.Create(doc.Table);
        if (tableResult.IsFailure)
        {
            return Result<UniqueCandidateProfile>.Failure(
                DecorateCoordinateMetadata(tableResult.Errors, doc.Schema, doc.Table, doc.Column));
        }

        var columnResult = ColumnName.Create(doc.Column);
        if (columnResult.IsFailure)
        {
            return Result<UniqueCandidateProfile>.Failure(
                DecorateCoordinateMetadata(columnResult.Errors, doc.Schema, doc.Table, doc.Column));
        }

        var status = MapProbeStatus(doc.ProbeStatus, defaultSampleSize: 0);

        return UniqueCandidateProfile.Create(
            schemaResult.Value,
            tableResult.Value,
            columnResult.Value,
            doc.HasDuplicate,
            status);
    }

    private static Result<ForeignKeyReality> MapForeignKey(ForeignKeyDocument doc)
    {
        if (doc.Reference is null)
        {
            return Result<ForeignKeyReality>.Failure(ValidationError.Create("profile.foreignKey.reference.missing", "Foreign key entries must include reference metadata."));
        }

        var fromSchemaResult = SchemaName.Create(doc.Reference.FromSchema);
        if (fromSchemaResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(
                DecorateForeignKeyMetadata(fromSchemaResult.Errors, doc.Reference, isSource: true));
        }

        var fromTableResult = TableName.Create(doc.Reference.FromTable);
        if (fromTableResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(
                DecorateForeignKeyMetadata(fromTableResult.Errors, doc.Reference, isSource: true));
        }

        var fromColumnResult = ColumnName.Create(doc.Reference.FromColumn);
        if (fromColumnResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(
                DecorateForeignKeyMetadata(fromColumnResult.Errors, doc.Reference, isSource: true));
        }

        var toSchemaResult = SchemaName.Create(doc.Reference.ToSchema);
        if (toSchemaResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(
                DecorateForeignKeyMetadata(toSchemaResult.Errors, doc.Reference, isSource: false));
        }

        var toTableResult = TableName.Create(doc.Reference.ToTable);
        if (toTableResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(
                DecorateForeignKeyMetadata(toTableResult.Errors, doc.Reference, isSource: false));
        }

        var toColumnResult = ColumnName.Create(doc.Reference.ToColumn);
        if (toColumnResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(
                DecorateForeignKeyMetadata(toColumnResult.Errors, doc.Reference, isSource: false));
        }

        var referenceResult = ForeignKeyReference.Create(
            fromSchemaResult.Value,
            fromTableResult.Value,
            fromColumnResult.Value,
            toSchemaResult.Value,
            toTableResult.Value,
            toColumnResult.Value,
            doc.Reference.HasDbConstraint);

        if (referenceResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(referenceResult.Errors);
        }

        var status = MapProbeStatus(doc.ProbeStatus, defaultSampleSize: 0);
        var orphanSample = MapForeignKeySample(doc.OrphanSample, doc.OrphanCount);

        return ForeignKeyReality.Create(
            referenceResult.Value,
            doc.HasOrphan,
            doc.OrphanCount,
            doc.IsNoCheck,
            status,
            orphanSample);
    }

    private static Result<CompositeUniqueCandidateProfile> MapCompositeUnique(CompositeUniqueCandidateDocument doc)
    {
        var schemaResult = SchemaName.Create(doc.Schema);
        if (schemaResult.IsFailure)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(
                DecorateCoordinateMetadata(schemaResult.Errors, doc.Schema, doc.Table, column: null));
        }

        var tableResult = TableName.Create(doc.Table);
        if (tableResult.IsFailure)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(
                DecorateCoordinateMetadata(tableResult.Errors, doc.Schema, doc.Table, column: null));
        }

        if (doc.Columns is null)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(
                ValidationError.Create("profile.compositeUnique.columns.missing", "Composite unique entries must specify 'Columns'."));
        }

        var columnResults = MapCompositeColumns(doc.Columns, doc.Schema, doc.Table);
        if (columnResults.IsFailure)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(columnResults.Errors);
        }

        return CompositeUniqueCandidateProfile.Create(schemaResult.Value, tableResult.Value, columnResults.Value, doc.HasDuplicate);
    }

    private static ImmutableArray<ValidationError> DecorateCoordinateMetadata(
        ImmutableArray<ValidationError> errors,
        string? schema,
        string? table,
        string? column)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return errors;
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(errors.Length);
        foreach (var error in errors)
        {
            builder.Add(error.WithMetadata(CreateCoordinateMetadata(schema, table, column)));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ValidationError> DecorateForeignKeyMetadata(
        ImmutableArray<ValidationError> errors,
        ForeignKeyReferenceDocument reference,
        bool isSource)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return errors;
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(errors.Length);
        foreach (var error in errors)
        {
            builder.Add(error.WithMetadata(CreateForeignKeyMetadata(reference, isSource)));
        }

        return builder.ToImmutable();
    }

    private static Result<ImmutableArray<ColumnName>> MapCompositeColumns(string[] columns, string? schema, string? table)
    {
        var builder = ImmutableArray.CreateBuilder<ColumnName>(columns.Length);
        for (var i = 0; i < columns.Length; i++)
        {
            var value = columns[i];
            var result = ColumnName.Create(value);
            if (result.IsFailure)
            {
                return Result<ImmutableArray<ColumnName>>.Failure(
                    DecorateCoordinateMetadata(result.Errors, schema, table, value));
            }

            builder.Add(result.Value);
        }

        return Result<ImmutableArray<ColumnName>>.Success(builder.ToImmutable());
    }

    private static IEnumerable<KeyValuePair<string, string?>> CreateCoordinateMetadata(
        string? schema,
        string? table,
        string? column)
    {
        yield return new KeyValuePair<string, string?>("schema", schema);
        yield return new KeyValuePair<string, string?>("table", table);
        yield return new KeyValuePair<string, string?>("column", column);
    }

    private static IEnumerable<KeyValuePair<string, string?>> CreateForeignKeyMetadata(
        ForeignKeyReferenceDocument reference,
        bool isSource)
    {
        yield return new KeyValuePair<string, string?>(isSource ? "from.schema" : "to.schema", isSource ? reference.FromSchema : reference.ToSchema);
        yield return new KeyValuePair<string, string?>(isSource ? "from.table" : "to.table", isSource ? reference.FromTable : reference.ToTable);
        yield return new KeyValuePair<string, string?>(isSource ? "from.column" : "to.column", isSource ? reference.FromColumn : reference.ToColumn);
    }

    private static ProfilingProbeStatus MapProbeStatus(ProfilingProbeStatusDocument? document, long defaultSampleSize)
    {
        if (document is null)
        {
            return ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, defaultSampleSize);
        }

        var capturedAt = document.CapturedAtUtc ?? DateTimeOffset.UnixEpoch;
        var sampleSize = document.SampleSize ?? defaultSampleSize;
        var outcome = document.Outcome ?? ProfilingProbeOutcome.Succeeded;

        return new ProfilingProbeStatus(capturedAt, sampleSize, outcome);
    }

    private static NullRowSample? MapNullSample(NullRowSampleDocument? document)
    {
        if (document is null || document.TotalNullRows <= 0)
        {
            return null;
        }

        var primaryKeyColumns = document.PrimaryKeyColumns?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
        var rows = document.Rows is null
            ? ImmutableArray<NullRowIdentifier>.Empty
            : document.Rows
                .Select(row => new NullRowIdentifier(row.PrimaryKeyValues?.Select(value => (object?)value).ToImmutableArray() ?? ImmutableArray<object?>.Empty))
                .ToImmutableArray();

        return NullRowSample.Create(primaryKeyColumns, rows, document.TotalNullRows);
    }

    private static ForeignKeyOrphanSample? MapForeignKeySample(ForeignKeyOrphanSampleDocument? document, long fallbackOrphanCount)
    {
        if (document is null || document.TotalOrphans <= 0)
        {
            if (fallbackOrphanCount <= 0)
            {
                return null;
            }

            return ForeignKeyOrphanSample.Create(ImmutableArray<string>.Empty, string.Empty, ImmutableArray<ForeignKeyOrphanIdentifier>.Empty, fallbackOrphanCount);
        }

        var primaryKeyColumns = document.PrimaryKeyColumns?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
        var rows = document.Rows is null
            ? ImmutableArray<ForeignKeyOrphanIdentifier>.Empty
            : document.Rows
                .Select(row => new ForeignKeyOrphanIdentifier(
                    row.PrimaryKeyValues?.Select(value => (object?)value).ToImmutableArray() ?? ImmutableArray<object?>.Empty,
                    row.ForeignKeyValue))
                .ToImmutableArray();

        return ForeignKeyOrphanSample.Create(primaryKeyColumns, document.ForeignKeyColumn, rows, document.TotalOrphans);
    }

    private sealed record ProfileSnapshotDocument
    {
        [JsonPropertyName("columns")]
        public ColumnDocument[]? Columns { get; init; }

        [JsonPropertyName("uniqueCandidates")]
        public UniqueCandidateDocument[]? UniqueCandidates { get; init; }

        [JsonPropertyName("compositeUniqueCandidates")]
        public CompositeUniqueCandidateDocument[]? CompositeUniqueCandidates { get; init; }

        [JsonPropertyName("fkReality")]
        public ForeignKeyDocument[]? ForeignKeys { get; init; }
    }

    private sealed record ColumnDocument
    {
        [JsonPropertyName("Schema")]
        public string Schema { get; init; } = string.Empty;

        [JsonPropertyName("Table")]
        public string Table { get; init; } = string.Empty;

        [JsonPropertyName("Column")]
        public string Column { get; init; } = string.Empty;

        [JsonPropertyName("IsNullablePhysical")]
        public bool IsNullablePhysical { get; init; }

        [JsonPropertyName("IsComputed")]
        public bool IsComputed { get; init; }

        [JsonPropertyName("IsPrimaryKey")]
        public bool IsPrimaryKey { get; init; }

        [JsonPropertyName("IsUniqueKey")]
        public bool IsUniqueKey { get; init; }

        [JsonPropertyName("DefaultDefinition")]
        public string? DefaultDefinition { get; init; }

        [JsonPropertyName("RowCount")]
        public long RowCount { get; init; }

        [JsonPropertyName("NullCount")]
        public long NullCount { get; init; }

        [JsonPropertyName("NullCountStatus")]
        public ProfilingProbeStatusDocument? NullCountStatus { get; init; }

        [JsonPropertyName("NullSample")]
        public NullRowSampleDocument? NullSample { get; init; }
    }

    private sealed record UniqueCandidateDocument
    {
        [JsonPropertyName("Schema")]
        public string Schema { get; init; } = string.Empty;

        [JsonPropertyName("Table")]
        public string Table { get; init; } = string.Empty;

        [JsonPropertyName("Column")]
        public string Column { get; init; } = string.Empty;

        [JsonPropertyName("HasDuplicate")]
        public bool HasDuplicate { get; init; }

        [JsonPropertyName("ProbeStatus")]
        public ProfilingProbeStatusDocument? ProbeStatus { get; init; }
    }

    private sealed record CompositeUniqueCandidateDocument
    {
        [JsonPropertyName("Schema")]
        public string Schema { get; init; } = string.Empty;

        [JsonPropertyName("Table")]
        public string Table { get; init; } = string.Empty;

        [JsonPropertyName("Columns")]
        public string[]? Columns { get; init; }

        [JsonPropertyName("HasDuplicate")]
        public bool HasDuplicate { get; init; }
    }

    private sealed record ForeignKeyDocument
    {
        [JsonPropertyName("Reference")]
        public ForeignKeyReferenceDocument? Reference { get; init; }

        [JsonPropertyName("HasOrphan")]
        public bool HasOrphan { get; init; }

        [JsonPropertyName("OrphanCount")]
        public long OrphanCount { get; init; }

        [JsonPropertyName("IsNoCheck")]
        public bool IsNoCheck { get; init; }

        [JsonPropertyName("ProbeStatus")]
        public ProfilingProbeStatusDocument? ProbeStatus { get; init; }

        [JsonPropertyName("OrphanSample")]
        public ForeignKeyOrphanSampleDocument? OrphanSample { get; init; }
    }

    private sealed record ForeignKeyReferenceDocument
    {
        [JsonPropertyName("FromSchema")]
        public string FromSchema { get; init; } = string.Empty;

        [JsonPropertyName("FromTable")]
        public string FromTable { get; init; } = string.Empty;

        [JsonPropertyName("FromColumn")]
        public string FromColumn { get; init; } = string.Empty;

        [JsonPropertyName("ToSchema")]
        public string ToSchema { get; init; } = string.Empty;

        [JsonPropertyName("ToTable")]
        public string ToTable { get; init; } = string.Empty;

        [JsonPropertyName("ToColumn")]
        public string ToColumn { get; init; } = string.Empty;

        [JsonPropertyName("HasDbConstraint")]
        public bool HasDbConstraint { get; init; }
    }

    private sealed record NullRowSampleDocument
    {
        [JsonPropertyName("PrimaryKeyColumns")]
        public string[]? PrimaryKeyColumns { get; init; }

        [JsonPropertyName("Rows")]
        public NullRowIdentifierDocument[]? Rows { get; init; }

        [JsonPropertyName("TotalNullRows")]
        public long TotalNullRows { get; init; }

        [JsonPropertyName("IsTruncated")]
        public bool IsTruncated { get; init; }
    }

    private sealed record NullRowIdentifierDocument
    {
        [JsonPropertyName("PrimaryKeyValues")]
        public string?[]? PrimaryKeyValues { get; init; }
    }

    private sealed record ForeignKeyOrphanSampleDocument
    {
        [JsonPropertyName("PrimaryKeyColumns")]
        public string[]? PrimaryKeyColumns { get; init; }

        [JsonPropertyName("ForeignKeyColumn")]
        public string ForeignKeyColumn { get; init; } = string.Empty;

        [JsonPropertyName("Rows")]
        public ForeignKeyOrphanRowDocument[]? Rows { get; init; }

        [JsonPropertyName("TotalOrphans")]
        public long TotalOrphans { get; init; }

        [JsonPropertyName("IsTruncated")]
        public bool IsTruncated { get; init; }
    }

    private sealed record ForeignKeyOrphanRowDocument
    {
        [JsonPropertyName("PrimaryKeyValues")]
        public string?[]? PrimaryKeyValues { get; init; }

        [JsonPropertyName("ForeignKeyValue")]
        public string? ForeignKeyValue { get; init; }
    }

    private sealed record ProfilingProbeStatusDocument
    {
        [JsonPropertyName("CapturedAtUtc")]
        public DateTimeOffset? CapturedAtUtc { get; init; }

        [JsonPropertyName("Outcome")]
        public ProfilingProbeOutcome? Outcome { get; init; }

        [JsonPropertyName("SampleSize")]
        public long? SampleSize { get; init; }
    }
}
