using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Profiling.Insights;

public sealed record ProfileInsightReport(ImmutableArray<ProfileInsight> Insights)
{
    public static ProfileInsightReport Empty { get; } = new(ImmutableArray<ProfileInsight>.Empty);

    public static Result<ProfileInsightReport> Create(IEnumerable<ProfileInsight> insights)
    {
        if (insights is null)
        {
            throw new ArgumentNullException(nameof(insights));
        }

        var materialized = insights.ToImmutableArray();
        if (materialized.IsDefault)
        {
            materialized = ImmutableArray<ProfileInsight>.Empty;
        }

        return Result<ProfileInsightReport>.Success(new ProfileInsightReport(materialized));
    }
}
