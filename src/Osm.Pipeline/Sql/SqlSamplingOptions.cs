using System;

namespace Osm.Pipeline.Sql;

public sealed record SqlSamplingOptions(long RowCountSamplingThreshold, int SampleSize)
{
    public static SqlSamplingOptions Default { get; } = new(250_000, 50_000);

    public static SqlSamplingOptions Create(long threshold, int sampleSize)
    {
        if (threshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Sampling threshold must be positive.");
        }

        if (sampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSize), "Sample size must be positive.");
        }

        return new SqlSamplingOptions(threshold, sampleSize);
    }
}
