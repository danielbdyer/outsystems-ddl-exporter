using System.Collections.Immutable;
using Osm.Validation.Tightening.Signals;

namespace Osm.Validation.Tightening;

public sealed record NullabilityDecision(
    ColumnCoordinate Column,
    bool MakeNotNull,
    bool RequiresRemediation,
    ImmutableArray<string> Rationales,
    SignalEvaluation? Trace)
{
    public static NullabilityDecision Create(
        ColumnCoordinate column,
        bool makeNotNull,
        bool requiresRemediation,
        ImmutableArray<string> rationales,
        SignalEvaluation? trace = null)
    {
        if (rationales.IsDefault)
        {
            rationales = ImmutableArray<string>.Empty;
        }

        return new NullabilityDecision(column, makeNotNull, requiresRemediation, rationales, trace);
    }
}
