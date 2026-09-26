using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Domain.Model;

public sealed record AttributeMetadata(
    string? Description,
    ImmutableArray<ExtendedProperty> ExtendedProperties)
{
    public static readonly AttributeMetadata Empty = new((string?)null, ExtendedProperty.EmptyArray);

    public static AttributeMetadata Create(
        string? description,
        IEnumerable<ExtendedProperty>? extendedProperties = null)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description!.Trim();
        var properties = (extendedProperties ?? Enumerable.Empty<ExtendedProperty>()).ToImmutableArray();
        if (properties.IsDefault)
        {
            properties = ExtendedProperty.EmptyArray;
        }

        return new AttributeMetadata(normalized, properties);
    }
}
