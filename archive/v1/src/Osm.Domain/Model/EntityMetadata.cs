using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Domain.Model;

public sealed record EntityMetadata(
    string? Description,
    ImmutableArray<ExtendedProperty> ExtendedProperties,
    TemporalTableMetadata Temporal)
{
    public static readonly EntityMetadata Empty = new((string?)null, ExtendedProperty.EmptyArray, TemporalTableMetadata.None);

    public static EntityMetadata Create(
        string? description,
        IEnumerable<ExtendedProperty>? extendedProperties = null,
        TemporalTableMetadata? temporal = null)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description!.Trim();
        var properties = (extendedProperties ?? Enumerable.Empty<ExtendedProperty>()).ToImmutableArray();
        if (properties.IsDefault)
        {
            properties = ExtendedProperty.EmptyArray;
        }

        return new EntityMetadata(normalized, properties, temporal ?? TemporalTableMetadata.None);
    }
}
