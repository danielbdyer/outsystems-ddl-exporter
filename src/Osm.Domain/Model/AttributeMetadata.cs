namespace Osm.Domain.Model;

public sealed record AttributeMetadata(string? Description)
{
    public static readonly AttributeMetadata Empty = new((string?)null);

    public static AttributeMetadata Create(string? description)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description!.Trim();
        return new AttributeMetadata(Description: normalized);
    }
}
