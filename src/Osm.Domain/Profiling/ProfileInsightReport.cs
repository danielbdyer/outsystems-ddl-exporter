using System;
using System.Collections.Immutable;

namespace Osm.Domain.Profiling;

public sealed record ProfileInsightReport(ImmutableArray<ProfileInsightModule> Modules)
{
    public static ProfileInsightReport Empty { get; } = new(ImmutableArray<ProfileInsightModule>.Empty);
}

public sealed record ProfileInsightModule(
    string Schema,
    string Table,
    ImmutableArray<ProfileInsight> Insights);

public sealed record ProfileInsight(ProfileInsightSeverity Severity, string Message);

public enum ProfileInsightSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}
