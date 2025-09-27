namespace Osm.Domain.Model;

public sealed record AttributeReality(
    bool? IsNullableInDatabase,
    bool? HasNulls,
    bool? HasDuplicates,
    bool? HasOrphans,
    bool IsPresentButInactive)
{
    public static readonly AttributeReality Unknown = new(null, null, null, null, false);
}
