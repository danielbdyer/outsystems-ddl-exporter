using System;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Model;

public sealed record IndexPartitionCompression(int PartitionNumber, string Compression)
{
    public static Result<IndexPartitionCompression> Create(int partitionNumber, string? compression)
    {
        if (partitionNumber <= 0)
        {
            return Result<IndexPartitionCompression>.Failure(
                ValidationError.Create("index.compression.partition.invalid", "Partition numbers must be positive."));
        }

        if (string.IsNullOrWhiteSpace(compression))
        {
            return Result<IndexPartitionCompression>.Failure(
                ValidationError.Create("index.compression.kind.invalid", "Compression level must be provided."));
        }

        return Result<IndexPartitionCompression>.Success(new IndexPartitionCompression(partitionNumber, compression.Trim()));
    }
}
