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
        var coordinate = ResolveCoordinate(doc.Schema, doc.Table, doc.Column);
        if (coordinate.IsFailure)
        {
            return Result<ColumnProfile>.Failure(coordinate.Errors);
        }

        var status = MapProbeStatus(doc.NullCountStatus, doc.RowCount);
        var nullSample = MapNullSample(doc.NullSample);

        return ColumnProfile.Create(
            coordinate.Value.Schema,
            coordinate.Value.Table,
            coordinate.Value.Column,
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
        var coordinate = ResolveCoordinate(doc.Schema, doc.Table, doc.Column);
        if (coordinate.IsFailure)
        {
            return Result<UniqueCandidateProfile>.Failure(coordinate.Errors);
        }

        var status = MapProbeStatus(doc.ProbeStatus, defaultSampleSize: 0);

        return UniqueCandidateProfile.Create(
            coordinate.Value.Schema,
            coordinate.Value.Table,
            coordinate.Value.Column,
            doc.HasDuplicate,
            status);
    }

    private static Result<ForeignKeyReality> MapForeignKey(ForeignKeyDocument doc)
    {
        var reference = doc.Reference ?? doc.Ref;
        if (reference is null)
        {
            return Result<ForeignKeyReality>.Failure(ValidationError.Create("profile.foreignKey.reference.missing", "Foreign key entries must include reference metadata."));
        }

        var fromCoordinate = ResolveForeignKeyCoordinate(
            reference.FromSchema, reference.FromTable, reference.FromColumn, reference, isSource: true);
        if (fromCoordinate.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(fromCoordinate.Errors);
        }

        var toCoordinate = ResolveForeignKeyCoordinate(
            reference.ToSchema, reference.ToTable, reference.ToColumn, reference, isSource: false);
        if (toCoordinate.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(toCoordinate.Errors);
        }

        var referenceResult = ForeignKeyReference.Create(
            fromCoordinate.Value.Schema,
            fromCoordinate.Value.Table,
            fromCoordinate.Value.Column,
            toCoordinate.Value.Schema,
            toCoordinate.Value.Table,
            toCoordinate.Value.Column,
            reference.HasDbConstraint);

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

    private static Result<(SchemaName Schema, TableName Table, ColumnName Column)> ResolveCoordinate(
        string? schema,
        string? table,
        string? column)
    {
        var schemaResult = SchemaName.Create(schema);
        if (schemaResult.IsFailure)
        {
            return Result<(SchemaName, TableName, ColumnName)>.Failure(
                DecorateCoordinateMetadata(schemaResult.Errors, schema, table, column));
        }

        var tableResult = TableName.Create(table);
        if (tableResult.IsFailure)
        {
            return Result<(SchemaName, TableName, ColumnName)>.Failure(
                DecorateCoordinateMetadata(tableResult.Errors, schema, table, column));
        }

        var columnResult = ColumnName.Create(column);
        if (columnResult.IsFailure)
        {
            return Result<(SchemaName, TableName, ColumnName)>.Failure(
                DecorateCoordinateMetadata(columnResult.Errors, schema, table, column));
        }

        return (schemaResult.Value, tableResult.Value, columnResult.Value);
    }

    private static Result<(SchemaName Schema, TableName Table, ColumnName Column)> ResolveForeignKeyCoordinate(
        string? schema,
        string? table,
        string? column,
        ForeignKeyReferenceDocument reference,
        bool isSource)
    {
        var schemaResult = SchemaName.Create(schema);
        if (schemaResult.IsFailure)
        {
            return Result<(SchemaName, TableName, ColumnName)>.Failure(
                DecorateForeignKeyMetadata(schemaResult.Errors, reference, isSource));
        }

        var tableResult = TableName.Create(table);
        if (tableResult.IsFailure)
        {
            return Result<(SchemaName, TableName, ColumnName)>.Failure(
                DecorateForeignKeyMetadata(tableResult.Errors, reference, isSource));
        }

        var columnResult = ColumnName.Create(column);
        if (columnResult.IsFailure)
        {
            return Result<(SchemaName, TableName, ColumnName)>.Failure(
                DecorateForeignKeyMetadata(columnResult.Errors, reference, isSource));
        }

        return (schemaResult.Value, tableResult.Value, columnResult.Value);
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
}
