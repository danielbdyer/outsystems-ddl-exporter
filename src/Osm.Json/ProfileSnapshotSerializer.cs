using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Profiling;

namespace Osm.Json;

public interface IProfileSnapshotSerializer
{
    Task SerializeAsync(ProfileSnapshot snapshot, Stream destination, CancellationToken cancellationToken = default);
}

public sealed class ProfileSnapshotSerializer : IProfileSnapshotSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SerializeAsync(ProfileSnapshot snapshot, Stream destination, CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var document = new ProfileSnapshotDocument
        {
            Columns = snapshot.Columns.Select(MapColumn).ToArray(),
            UniqueCandidates = snapshot.UniqueCandidates.Select(MapUniqueCandidate).ToArray(),
            CompositeUniqueCandidates = snapshot.CompositeUniqueCandidates.Select(MapCompositeUniqueCandidate).ToArray(),
            ForeignKeys = snapshot.ForeignKeys.Select(MapForeignKey).ToArray()
        };

        await JsonSerializer.SerializeAsync(destination, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static ColumnDocument MapColumn(ColumnProfile profile)
    {
        return new ColumnDocument
        {
            Schema = profile.Schema.Value,
            Table = profile.Table.Value,
            Column = profile.Column.Value,
            IsNullablePhysical = profile.IsNullablePhysical,
            IsComputed = profile.IsComputed,
            IsPrimaryKey = profile.IsPrimaryKey,
            IsUniqueKey = profile.IsUniqueKey,
            DefaultDefinition = profile.DefaultDefinition,
            RowCount = profile.RowCount,
            NullCount = profile.NullCount,
            NullCountStatus = MapProbeStatus(profile.NullCountStatus),
            NullSample = MapNullSample(profile.NullRowSample)
        };
    }

    private static UniqueCandidateDocument MapUniqueCandidate(UniqueCandidateProfile profile)
    {
        return new UniqueCandidateDocument
        {
            Schema = profile.Schema.Value,
            Table = profile.Table.Value,
            Column = profile.Column.Value,
            HasDuplicate = profile.HasDuplicate,
            ProbeStatus = MapProbeStatus(profile.ProbeStatus)
        };
    }

    private static CompositeUniqueCandidateDocument MapCompositeUniqueCandidate(CompositeUniqueCandidateProfile profile)
    {
        return new CompositeUniqueCandidateDocument
        {
            Schema = profile.Schema.Value,
            Table = profile.Table.Value,
            Columns = profile.Columns.Select(column => column.Value).ToArray(),
            HasDuplicate = profile.HasDuplicate
        };
    }

    private static ForeignKeyDocument MapForeignKey(ForeignKeyReality reality)
    {
        return new ForeignKeyDocument
        {
            Reference = new ForeignKeyReferenceDocument
            {
                FromSchema = reality.Reference.FromSchema.Value,
                FromTable = reality.Reference.FromTable.Value,
                FromColumn = reality.Reference.FromColumn.Value,
                ToSchema = reality.Reference.ToSchema.Value,
                ToTable = reality.Reference.ToTable.Value,
                ToColumn = reality.Reference.ToColumn.Value,
                HasDbConstraint = reality.Reference.HasDatabaseConstraint
            },
            HasOrphan = reality.HasOrphan,
            OrphanCount = reality.OrphanCount,
            IsNoCheck = reality.IsNoCheck,
            ProbeStatus = MapProbeStatus(reality.ProbeStatus),
            OrphanSample = MapForeignKeySample(reality.OrphanSample)
        };
    }

    private static ProfilingProbeStatusDocument MapProbeStatus(ProfilingProbeStatus status)
    {
        return new ProfilingProbeStatusDocument
        {
            CapturedAtUtc = status.CapturedAtUtc,
            Outcome = status.Outcome,
            SampleSize = status.SampleSize
        };
    }

    private static NullRowSampleDocument? MapNullSample(NullRowSample? sample)
    {
        if (sample is null || sample.TotalNullRows <= 0)
        {
            return null;
        }

        return new NullRowSampleDocument
        {
            PrimaryKeyColumns = sample.PrimaryKeyColumns.ToArray(),
            Rows = sample.SampleRows
                .Select(row => new NullRowIdentifierDocument
                {
                    PrimaryKeyValues = row.PrimaryKeyValues.Select(FormatSampleValue).ToArray()
                })
                .ToArray(),
            TotalNullRows = sample.TotalNullRows,
            IsTruncated = sample.IsTruncated
        };
    }

    private static ForeignKeyOrphanSampleDocument? MapForeignKeySample(ForeignKeyOrphanSample? sample)
    {
        if (sample is null || sample.TotalOrphans <= 0)
        {
            return null;
        }

        return new ForeignKeyOrphanSampleDocument
        {
            PrimaryKeyColumns = sample.PrimaryKeyColumns.ToArray(),
            ForeignKeyColumn = sample.ForeignKeyColumn,
            Rows = sample.SampleRows
                .Select(row => new ForeignKeyOrphanRowDocument
                {
                    PrimaryKeyValues = row.PrimaryKeyValues.Select(FormatSampleValue).ToArray(),
                    ForeignKeyValue = FormatSampleValue(row.ForeignKeyValue)
                })
                .ToArray(),
            TotalOrphans = sample.TotalOrphans,
            IsTruncated = sample.IsTruncated
        };
    }

    private static string? FormatSampleValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }

    private sealed record ProfileSnapshotDocument
    {
        [JsonPropertyName("columns")]
        public ColumnDocument[] Columns { get; init; } = Array.Empty<ColumnDocument>();

        [JsonPropertyName("uniqueCandidates")]
        public UniqueCandidateDocument[] UniqueCandidates { get; init; } = Array.Empty<UniqueCandidateDocument>();

        [JsonPropertyName("compositeUniqueCandidates")]
        public CompositeUniqueCandidateDocument[] CompositeUniqueCandidates { get; init; } = Array.Empty<CompositeUniqueCandidateDocument>();

        [JsonPropertyName("fkReality")]
        public ForeignKeyDocument[] ForeignKeys { get; init; } = Array.Empty<ForeignKeyDocument>();
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
        public ProfilingProbeStatusDocument NullCountStatus { get; init; } = new();

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
        public ProfilingProbeStatusDocument ProbeStatus { get; init; } = new();
    }

    private sealed record CompositeUniqueCandidateDocument
    {
        [JsonPropertyName("Schema")]
        public string Schema { get; init; } = string.Empty;

        [JsonPropertyName("Table")]
        public string Table { get; init; } = string.Empty;

        [JsonPropertyName("Columns")]
        public string[] Columns { get; init; } = Array.Empty<string>();

        [JsonPropertyName("HasDuplicate")]
        public bool HasDuplicate { get; init; }
    }

    private sealed record ForeignKeyDocument
    {
        [JsonPropertyName("Reference")]
        public ForeignKeyReferenceDocument Reference { get; init; } = new();

        [JsonPropertyName("HasOrphan")]
        public bool HasOrphan { get; init; }

        [JsonPropertyName("OrphanCount")]
        public long OrphanCount { get; init; }

        [JsonPropertyName("IsNoCheck")]
        public bool IsNoCheck { get; init; }

        [JsonPropertyName("ProbeStatus")]
        public ProfilingProbeStatusDocument ProbeStatus { get; init; } = new();

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
        public string[] PrimaryKeyColumns { get; init; } = Array.Empty<string>();

        [JsonPropertyName("Rows")]
        public NullRowIdentifierDocument[] Rows { get; init; } = Array.Empty<NullRowIdentifierDocument>();

        [JsonPropertyName("TotalNullRows")]
        public long TotalNullRows { get; init; }

        [JsonPropertyName("IsTruncated")]
        public bool IsTruncated { get; init; }
    }

    private sealed record NullRowIdentifierDocument
    {
        [JsonPropertyName("PrimaryKeyValues")]
        public string?[] PrimaryKeyValues { get; init; } = Array.Empty<string?>();
    }

    private sealed record ForeignKeyOrphanSampleDocument
    {
        [JsonPropertyName("PrimaryKeyColumns")]
        public string[] PrimaryKeyColumns { get; init; } = Array.Empty<string>();

        [JsonPropertyName("ForeignKeyColumn")]
        public string ForeignKeyColumn { get; init; } = string.Empty;

        [JsonPropertyName("Rows")]
        public ForeignKeyOrphanRowDocument[] Rows { get; init; } = Array.Empty<ForeignKeyOrphanRowDocument>();

        [JsonPropertyName("TotalOrphans")]
        public long TotalOrphans { get; init; }

        [JsonPropertyName("IsTruncated")]
        public bool IsTruncated { get; init; }
    }

    private sealed record ForeignKeyOrphanRowDocument
    {
        [JsonPropertyName("PrimaryKeyValues")]
        public string?[] PrimaryKeyValues { get; init; } = Array.Empty<string?>();

        [JsonPropertyName("ForeignKeyValue")]
        public string? ForeignKeyValue { get; init; }
    }

    private sealed record ProfilingProbeStatusDocument
    {
        [JsonPropertyName("CapturedAtUtc")]
        public DateTimeOffset CapturedAtUtc { get; init; }

        [JsonPropertyName("Outcome")]
        public ProfilingProbeOutcome Outcome { get; init; }

        [JsonPropertyName("SampleSize")]
        public long SampleSize { get; init; }
    }
}
