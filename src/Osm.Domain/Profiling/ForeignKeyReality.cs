using Osm.Domain.Abstractions;

namespace Osm.Domain.Profiling;

public sealed record ForeignKeyReality(
    ForeignKeyReference Reference,
    bool HasOrphan)
{
    public static Result<ForeignKeyReality> Create(ForeignKeyReference reference, bool hasOrphan)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        return Result<ForeignKeyReality>.Success(new ForeignKeyReality(reference, hasOrphan));
    }
}
