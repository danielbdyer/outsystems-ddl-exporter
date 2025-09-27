using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public sealed record NullabilityDecision(
    ColumnCoordinate Column,
    bool MakeNotNull,
    bool RequiresRemediation,
    ImmutableArray<string> Rationales)
{
    public static NullabilityDecision Create(ColumnCoordinate column, bool makeNotNull, bool requiresRemediation, ImmutableArray<string> rationales)
    {
        if (rationales.IsDefault)
        {
            rationales = ImmutableArray<string>.Empty;
        }

        return new NullabilityDecision(column, makeNotNull, requiresRemediation, rationales);
    }
}
