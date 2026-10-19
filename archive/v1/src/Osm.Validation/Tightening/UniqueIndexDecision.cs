using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public sealed record UniqueIndexDecision(
    IndexCoordinate Index,
    bool EnforceUnique,
    bool RequiresRemediation,
    ImmutableArray<string> Rationales)
{
    public static UniqueIndexDecision Create(IndexCoordinate index, bool enforceUnique, bool requiresRemediation, ImmutableArray<string> rationales)
    {
        if (rationales.IsDefault)
        {
            rationales = ImmutableArray<string>.Empty;
        }

        return new UniqueIndexDecision(index, enforceUnique, requiresRemediation, rationales);
    }
}
