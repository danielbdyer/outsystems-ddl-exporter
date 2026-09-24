using System;
using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Smo;

internal static class MsDescriptionResolver
{
    public static string? Resolve(EntityMetadata metadata)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        return Resolve(metadata.Description, metadata.ExtendedProperties);
    }

    public static string? Resolve(AttributeMetadata metadata)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        return Resolve(metadata.Description, metadata.ExtendedProperties);
    }

    public static string? Resolve(IndexModel index)
    {
        if (index is null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        return Resolve(description: null, index.ExtendedProperties);
    }

    private static string? Resolve(string? description, ImmutableArray<ExtendedProperty> extendedProperties)
    {
        var normalizedFromProperty = NormalizeWhitespace(GetMsDescriptionValue(extendedProperties));
        if (!string.IsNullOrWhiteSpace(normalizedFromProperty))
        {
            return normalizedFromProperty;
        }

        return NormalizeWhitespace(description);
    }

    private static string? GetMsDescriptionValue(ImmutableArray<ExtendedProperty> extendedProperties)
    {
        if (extendedProperties.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var property in extendedProperties)
        {
            if (property is null || string.IsNullOrWhiteSpace(property.Value))
            {
                continue;
            }

            if (string.Equals(property.Name, "MS_Description", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? NormalizeWhitespace(string? value)
    {
        return SmoNormalization.NormalizeWhitespace(value);
    }
}
