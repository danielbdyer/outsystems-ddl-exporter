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
}
