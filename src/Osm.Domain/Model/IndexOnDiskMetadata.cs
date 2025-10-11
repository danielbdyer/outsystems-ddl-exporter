using System.Collections.Immutable;

namespace Osm.Domain.Model;

public sealed record IndexOnDiskMetadata(
    IndexKind Kind,
    bool IsDisabled,
    bool IsPadded,
    int? FillFactor,
    bool IgnoreDuplicateKey,
    bool AllowRowLocks,
    bool AllowPageLocks,
    bool NoRecomputeStatistics,
    string? FilterDefinition,
    IndexDataSpace? DataSpace,
    ImmutableArray<IndexPartitionColumn> PartitionColumns,
    ImmutableArray<IndexPartitionCompression> DataCompression)
{
    public static readonly IndexOnDiskMetadata Empty = new(
        IndexKind.Unknown,
        IsDisabled: false,
        IsPadded: false,
        FillFactor: null,
        IgnoreDuplicateKey: false,
        AllowRowLocks: true,
        AllowPageLocks: true,
        NoRecomputeStatistics: false,
        FilterDefinition: null,
        DataSpace: null,
        ImmutableArray<IndexPartitionColumn>.Empty,
        ImmutableArray<IndexPartitionCompression>.Empty);

    public static IndexOnDiskMetadata Create(
        IndexKind kind,
        bool isDisabled,
        bool isPadded,
        int? fillFactor,
        bool ignoreDuplicateKey,
        bool allowRowLocks,
        bool allowPageLocks,
        bool noRecomputeStatistics,
        string? filterDefinition,
        IndexDataSpace? dataSpace,
        ImmutableArray<IndexPartitionColumn> partitionColumns,
        ImmutableArray<IndexPartitionCompression> dataCompression)
    {
        var normalizedFilter = string.IsNullOrWhiteSpace(filterDefinition) ? null : filterDefinition;
        var normalizedFillFactor = fillFactor is > 0 and <= 100 ? fillFactor : null;

        return new IndexOnDiskMetadata(
            kind,
            isDisabled,
            isPadded,
            normalizedFillFactor,
            ignoreDuplicateKey,
            allowRowLocks,
            allowPageLocks,
            noRecomputeStatistics,
            normalizedFilter,
            dataSpace,
            partitionColumns.IsDefault ? ImmutableArray<IndexPartitionColumn>.Empty : partitionColumns,
            dataCompression.IsDefault ? ImmutableArray<IndexPartitionCompression>.Empty : dataCompression);
    }
}
