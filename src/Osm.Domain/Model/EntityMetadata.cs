namespace Osm.Domain.Model;

public sealed record EntityMetadata(string? Description)
{
    public static readonly EntityMetadata Empty = new((string?)null);

    public static EntityMetadata Create(string? description)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description!.Trim();
        return new EntityMetadata(Description: normalized);
    }
}
