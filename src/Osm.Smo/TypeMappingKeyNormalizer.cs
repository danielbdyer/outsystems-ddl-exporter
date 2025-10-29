using System;

namespace Osm.Smo;

internal static class TypeMappingKeyNormalizer
{
    public static string Normalize(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return string.Empty;
        }

        var trimmed = dataType.Trim();
        if (trimmed.StartsWith("rt", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 2)
        {
            trimmed = trimmed[2..];
        }

        trimmed = trimmed
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return trimmed.ToLowerInvariant();
    }
}
