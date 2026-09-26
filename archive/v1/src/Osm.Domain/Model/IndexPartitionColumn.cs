using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record IndexPartitionColumn(ColumnName Column, int Ordinal)
{
    public static Result<IndexPartitionColumn> Create(ColumnName column, int ordinal)
    {
        if (ordinal <= 0)
        {
            return Result<IndexPartitionColumn>.Failure(
                ValidationError.Create("index.partitionColumn.ordinal.invalid", "Partition ordinals must be positive."));
        }

        return Result<IndexPartitionColumn>.Success(new IndexPartitionColumn(column, ordinal));
    }
}
