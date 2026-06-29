using System;
using System.Text.Json.Serialization;
using Osm.Domain.Profiling;

namespace Osm.Json;

// Shared wire-format DTOs for the profile snapshot, used by BOTH
// ProfileSnapshotSerializer (write) and ProfileSnapshotDeserializer (read).
// Previously each side declared its own private copy of this family, which had
// to be kept in lockstep by hand. The only read/write asymmetry is the foreign
// key reference alias: the reader accepts both "Reference" and the legacy "Ref",
// while the writer only ever emits "Reference" — preserved here via
// JsonIgnoreCondition.WhenWritingNull on Ref so serialized output is unchanged.

internal sealed record ProfileSnapshotDocument
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

internal sealed record ColumnDocument
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

internal sealed record UniqueCandidateDocument
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

internal sealed record CompositeUniqueCandidateDocument
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

internal sealed record ForeignKeyDocument
{
    [JsonPropertyName("Reference")]
    public ForeignKeyReferenceDocument? Reference { get; init; }

    [JsonPropertyName("Ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForeignKeyReferenceDocument? Ref { get; init; }

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

internal sealed record ForeignKeyReferenceDocument
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

internal sealed record NullRowSampleDocument
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

internal sealed record NullRowIdentifierDocument
{
    [JsonPropertyName("PrimaryKeyValues")]
    public string?[]? PrimaryKeyValues { get; init; }
}

internal sealed record ForeignKeyOrphanSampleDocument
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

internal sealed record ForeignKeyOrphanRowDocument
{
    [JsonPropertyName("PrimaryKeyValues")]
    public string?[]? PrimaryKeyValues { get; init; }

    [JsonPropertyName("ForeignKeyValue")]
    public string? ForeignKeyValue { get; init; }
}

internal sealed record ProfilingProbeStatusDocument
{
    [JsonPropertyName("CapturedAtUtc")]
    public DateTimeOffset? CapturedAtUtc { get; init; }

    [JsonPropertyName("Outcome")]
    public ProfilingProbeOutcome? Outcome { get; init; }

    [JsonPropertyName("SampleSize")]
    public long? SampleSize { get; init; }
}
