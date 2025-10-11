using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class IndexModelTests
{
    private static IndexColumnModel Column(string attribute, string column, int ordinal)
    {
        var attributeName = AttributeName.Create(attribute).Value;
        var columnName = ColumnName.Create(column).Value;
        return IndexColumnModel.Create(attributeName, columnName, ordinal, isIncluded: false, IndexColumnDirection.Ascending).Value;
    }

    [Fact]
    public void Create_ShouldFail_WhenColumnsEmpty()
    {
        var name = IndexName.Create("IDX_ENTITY").Value;

        var result = IndexModel.Create(name, isUnique: false, isPrimary: false, isPlatformAuto: false, Array.Empty<IndexColumnModel>());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "index.columns.empty");
    }

    [Fact]
    public void Create_ShouldFail_WhenDuplicateOrdinals()
    {
        var name = IndexName.Create("IDX_ENTITY").Value;
        var columnOne = Column("First", "FIRST", 1);
        var columnTwo = Column("Second", "SECOND", 1);

        var result = IndexModel.Create(name, isUnique: false, isPrimary: false, isPlatformAuto: false, new[] { columnOne, columnTwo });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "index.columns.duplicateOrdinal");
    }

    [Fact]
    public void Create_ShouldSucceed_WhenOrdinalsUnique()
    {
        var name = IndexName.Create("IDX_ENTITY").Value;
        var columnOne = Column("First", "FIRST", 1);
        var columnTwo = Column("Second", "SECOND", 2);

        var result = IndexModel.Create(name, isUnique: true, isPrimary: false, isPlatformAuto: true, new[] { columnOne, columnTwo });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Columns.Length);
        Assert.True(result.Value.IsPlatformAuto);
    }

    [Fact]
    public void Create_ShouldAttach_OnDiskMetadata()
    {
        var name = IndexName.Create("IDX_ENTITY").Value;
        var column = Column("First", "FIRST", 1);
        var metadata = IndexOnDiskMetadata.Create(
            IndexKind.UniqueIndex,
            isDisabled: true,
            isPadded: true,
            fillFactor: 75,
            ignoreDuplicateKey: false,
            allowRowLocks: true,
            allowPageLocks: false,
            noRecomputeStatistics: true,
            filterDefinition: "[FIRST] IS NOT NULL",
            dataSpace: IndexDataSpace.Create("PRIMARY", "ROWS_FILEGROUP"),
            ImmutableArray<IndexPartitionColumn>.Empty,
            ImmutableArray<IndexPartitionCompression>.Empty);

        var result = IndexModel.Create(
            name,
            isUnique: true,
            isPrimary: false,
            isPlatformAuto: false,
            new[] { column },
            metadata);

        Assert.True(result.IsSuccess);
        Assert.Equal(metadata, result.Value.OnDisk);
        Assert.Equal(IndexKind.UniqueIndex, result.Value.OnDisk.Kind);
    }
}
