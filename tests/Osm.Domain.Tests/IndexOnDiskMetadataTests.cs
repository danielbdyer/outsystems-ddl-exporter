using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class IndexOnDiskMetadataTests
{
    private static IndexPartitionColumn PartitionColumn(string column, int ordinal)
    {
        var columnName = ColumnName.Create(column).Value;
        return IndexPartitionColumn.Create(columnName, ordinal).Value;
    }

    private static IndexPartitionCompression Compression(int partition, string level)
        => IndexPartitionCompression.Create(partition, level).Value;

    [Fact]
    public void Create_ShouldNormalizeFilterDefinition()
    {
        var metadata = IndexOnDiskMetadata.Create(
            IndexKind.UniqueIndex,
            isDisabled: false,
            isPadded: false,
            fillFactor: 90,
            ignoreDuplicateKey: false,
            allowRowLocks: true,
            allowPageLocks: true,
            noRecomputeStatistics: false,
            filterDefinition: "   ",
            dataSpace: null,
            ImmutableArray<IndexPartitionColumn>.Empty,
            ImmutableArray<IndexPartitionCompression>.Empty);

        Assert.Null(metadata.FilterDefinition);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(105, null)]
    [InlineData(65, 65)]
    public void Create_ShouldNormalizeFillFactor(int? input, int? expected)
    {
        var metadata = IndexOnDiskMetadata.Create(
            IndexKind.PrimaryKey,
            isDisabled: false,
            isPadded: false,
            fillFactor: input,
            ignoreDuplicateKey: false,
            allowRowLocks: true,
            allowPageLocks: true,
            noRecomputeStatistics: false,
            filterDefinition: null,
            dataSpace: null,
            ImmutableArray<IndexPartitionColumn>.Empty,
            ImmutableArray<IndexPartitionCompression>.Empty);

        Assert.Equal(expected, metadata.FillFactor);
    }

    [Fact]
    public void Create_ShouldUseEmptyCollections_WhenDefaultsProvided()
    {
        var metadata = IndexOnDiskMetadata.Create(
            IndexKind.NonUniqueIndex,
            isDisabled: false,
            isPadded: false,
            fillFactor: 80,
            ignoreDuplicateKey: false,
            allowRowLocks: true,
            allowPageLocks: true,
            noRecomputeStatistics: false,
            filterDefinition: null,
            dataSpace: null,
            default,
            default);

        Assert.False(metadata.PartitionColumns.IsDefault);
        Assert.Empty(metadata.PartitionColumns);
        Assert.False(metadata.DataCompression.IsDefault);
        Assert.Empty(metadata.DataCompression);
    }

    [Fact]
    public void Create_ShouldPreserveExplicitCollections()
    {
        var columns = ImmutableArray.Create(PartitionColumn("RegionId", 1));
        var compression = ImmutableArray.Create(Compression(1, "PAGE"));

        var metadata = IndexOnDiskMetadata.Create(
            IndexKind.UniqueConstraint,
            isDisabled: false,
            isPadded: true,
            fillFactor: 70,
            ignoreDuplicateKey: true,
            allowRowLocks: false,
            allowPageLocks: false,
            noRecomputeStatistics: true,
            filterDefinition: "[RegionId] IS NOT NULL",
            dataSpace: IndexDataSpace.Create("PRIMARY", "rows"),
            columns,
            compression);

        Assert.Equal(columns, metadata.PartitionColumns);
        Assert.Equal(compression, metadata.DataCompression);
        Assert.Equal("[RegionId] IS NOT NULL", metadata.FilterDefinition);
        Assert.True(metadata.IgnoreDuplicateKey);
        Assert.True(metadata.NoRecomputeStatistics);
    }
}
