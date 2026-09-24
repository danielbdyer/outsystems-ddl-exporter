using System;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

internal static class TableSamplingPolicy
{
    public static bool ShouldSample(long rowCount, SqlProfilerOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (rowCount <= 0)
        {
            return false;
        }

        var threshold = options.Sampling.RowCountSamplingThreshold;
        if (options.Limits.MaxRowsPerTable.HasValue)
        {
            threshold = Math.Min(threshold, options.Limits.MaxRowsPerTable.Value);
        }

        return rowCount > threshold;
    }

    public static int GetSampleSize(long rowCount, SqlProfilerOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var sample = (long)options.Sampling.SampleSize;
        if (options.Limits.MaxRowsPerTable.HasValue)
        {
            sample = Math.Min(sample, options.Limits.MaxRowsPerTable.Value);
        }

        if (rowCount > 0)
        {
            sample = Math.Min(sample, rowCount);
        }

        sample = Math.Clamp(sample, 1, (long)int.MaxValue);
        return (int)sample;
    }

    public static long DetermineSampleSize(long rowCount, SqlProfilerOptions options)
    {
        var shouldSample = ShouldSample(rowCount, options);
        if (!shouldSample)
        {
            return Math.Max(0, rowCount);
        }

        return GetSampleSize(rowCount, options);
    }
}
