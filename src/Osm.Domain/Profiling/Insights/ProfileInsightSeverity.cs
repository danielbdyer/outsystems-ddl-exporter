using System.Diagnostics.CodeAnalysis;

namespace Osm.Domain.Profiling.Insights;

[SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute", Justification = "Severity values are discrete levels.")]
public enum ProfileInsightSeverity
{
    Info = 0,
    Recommendation = 1,
    Warning = 2,
    Risk = 3
}
