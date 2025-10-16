using System;

namespace Osm.Domain.Profiling;

public sealed record ProfilingInsightOptions
{
    public ProfilingInsightOptions(
        double highNullRatioThreshold,
        double nullFreeNullableThreshold,
        long minimumRowCountForRatioInsights,
        long minimumRowCountForOpportunities)
    {
        if (highNullRatioThreshold <= 0 || highNullRatioThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(highNullRatioThreshold), "High null ratio threshold must be between 0 and 1.");
        }

        if (nullFreeNullableThreshold < 0 || nullFreeNullableThreshold >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nullFreeNullableThreshold), "Null-free threshold must be between 0 (inclusive) and 1 (exclusive).");
        }

        if (minimumRowCountForRatioInsights < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRowCountForRatioInsights), "Minimum row count must be non-negative.");
        }

        if (minimumRowCountForOpportunities < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRowCountForOpportunities), "Minimum row count must be non-negative.");
        }

        HighNullRatioThreshold = highNullRatioThreshold;
        NullFreeNullableThreshold = nullFreeNullableThreshold;
        MinimumRowCountForRatioInsights = minimumRowCountForRatioInsights;
        MinimumRowCountForOpportunities = minimumRowCountForOpportunities;
    }

    public double HighNullRatioThreshold { get; }

    public double NullFreeNullableThreshold { get; }

    public long MinimumRowCountForRatioInsights { get; }

    public long MinimumRowCountForOpportunities { get; }

    public static ProfilingInsightOptions Default { get; } = new(0.35, 0.005, 100, 25);
}
