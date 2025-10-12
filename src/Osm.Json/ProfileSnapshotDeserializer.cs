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
        AllowTrailingCommas = true
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
            var message = JsonErrorFormatter.BuildMessage("Invalid profiling JSON payload", ex);
            return Result<ProfileSnapshot>.Failure(ValidationError.Create("profile.json.parseFailed", message));
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
            return Result<ColumnProfile>.Failure(schemaResult.Errors);
        }

        var tableResult = TableName.Create(doc.Table);
        if (tableResult.IsFailure)
        {
            return Result<ColumnProfile>.Failure(tableResult.Errors);
        }

        var columnResult = ColumnName.Create(doc.Column);
        if (columnResult.IsFailure)
        {
            return Result<ColumnProfile>.Failure(columnResult.Errors);
        }

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
            doc.NullCount);
    }

    private static Result<UniqueCandidateProfile> MapUniqueCandidate(UniqueCandidateDocument doc)
    {
        var schemaResult = SchemaName.Create(doc.Schema);
        if (schemaResult.IsFailure)
        {
            return Result<UniqueCandidateProfile>.Failure(schemaResult.Errors);
        }

        var tableResult = TableName.Create(doc.Table);
        if (tableResult.IsFailure)
        {
            return Result<UniqueCandidateProfile>.Failure(tableResult.Errors);
        }

        var columnResult = ColumnName.Create(doc.Column);
        if (columnResult.IsFailure)
        {
            return Result<UniqueCandidateProfile>.Failure(columnResult.Errors);
        }

        return UniqueCandidateProfile.Create(schemaResult.Value, tableResult.Value, columnResult.Value, doc.HasDuplicate);
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
            return Result<ForeignKeyReality>.Failure(fromSchemaResult.Errors);
        }

        var fromTableResult = TableName.Create(doc.Reference.FromTable);
        if (fromTableResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(fromTableResult.Errors);
        }

        var fromColumnResult = ColumnName.Create(doc.Reference.FromColumn);
        if (fromColumnResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(fromColumnResult.Errors);
        }

        var toSchemaResult = SchemaName.Create(doc.Reference.ToSchema);
        if (toSchemaResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(toSchemaResult.Errors);
        }

        var toTableResult = TableName.Create(doc.Reference.ToTable);
        if (toTableResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(toTableResult.Errors);
        }

        var toColumnResult = ColumnName.Create(doc.Reference.ToColumn);
        if (toColumnResult.IsFailure)
        {
            return Result<ForeignKeyReality>.Failure(toColumnResult.Errors);
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

        return ForeignKeyReality.Create(referenceResult.Value, doc.HasOrphan, doc.IsNoCheck);
    }

    private static Result<CompositeUniqueCandidateProfile> MapCompositeUnique(CompositeUniqueCandidateDocument doc)
    {
        var schemaResult = SchemaName.Create(doc.Schema);
        if (schemaResult.IsFailure)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(schemaResult.Errors);
        }

        var tableResult = TableName.Create(doc.Table);
        if (tableResult.IsFailure)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(tableResult.Errors);
        }

        if (doc.Columns is null)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(
                ValidationError.Create("profile.compositeUnique.columns.missing", "Composite unique entries must specify 'Columns'."));
        }

        var columnResults = Result.Collect(doc.Columns.Select(ColumnName.Create));
        if (columnResults.IsFailure)
        {
            return Result<CompositeUniqueCandidateProfile>.Failure(columnResults.Errors);
        }

        return CompositeUniqueCandidateProfile.Create(schemaResult.Value, tableResult.Value, columnResults.Value, doc.HasDuplicate);
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
        public string? Schema { get; init; }

        [JsonPropertyName("Table")]
        public string? Table { get; init; }

        [JsonPropertyName("Column")]
        public string? Column { get; init; }

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
    }

    private sealed record UniqueCandidateDocument
    {
        [JsonPropertyName("Schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("Table")]
        public string? Table { get; init; }

        [JsonPropertyName("Column")]
        public string? Column { get; init; }

        [JsonPropertyName("HasDuplicate")]
        public bool HasDuplicate { get; init; }
    }

    private sealed record CompositeUniqueCandidateDocument
    {
        [JsonPropertyName("Schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("Table")]
        public string? Table { get; init; }

        [JsonPropertyName("Columns")]
        public string[]? Columns { get; init; }

        [JsonPropertyName("HasDuplicate")]
        public bool HasDuplicate { get; init; }
    }

    private sealed record ForeignKeyDocument
    {
        [JsonPropertyName("Ref")]
        public ForeignKeyReferenceDocument? Reference { get; init; }

        [JsonPropertyName("HasOrphan")]
        public bool HasOrphan { get; init; }

        [JsonPropertyName("IsNoCheck")]
        public bool IsNoCheck { get; init; }
    }

    private sealed record ForeignKeyReferenceDocument
    {
        [JsonPropertyName("FromSchema")]
        public string? FromSchema { get; init; }

        [JsonPropertyName("FromTable")]
        public string? FromTable { get; init; }

        [JsonPropertyName("FromColumn")]
        public string? FromColumn { get; init; }

        [JsonPropertyName("ToSchema")]
        public string? ToSchema { get; init; }

        [JsonPropertyName("ToTable")]
        public string? ToTable { get; init; }

        [JsonPropertyName("ToColumn")]
        public string? ToColumn { get; init; }

        [JsonPropertyName("HasDbConstraint")]
        public bool HasDbConstraint { get; init; }
    }
}
