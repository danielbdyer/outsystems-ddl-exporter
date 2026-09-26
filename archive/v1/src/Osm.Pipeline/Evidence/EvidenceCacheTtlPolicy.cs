using System;
using System.Collections.Generic;
using System.Globalization;

namespace Osm.Pipeline.Evidence;

internal static class EvidenceCacheTtlPolicy
{
    public static DateTimeOffset? DetermineExpiry(
        DateTimeOffset createdAtUtc,
        IReadOnlyDictionary<string, string?> metadata)
    {
        if (TryGetTtl(metadata, out var ttl))
        {
            return createdAtUtc.Add(ttl);
        }

        return null;
    }

    public static bool TryGetTtl(
        IReadOnlyDictionary<string, string?> metadata,
        out TimeSpan ttl)
    {
        ttl = TimeSpan.Zero;
        if (!metadata.TryGetValue("cache.ttlSeconds", out var ttlValue) || string.IsNullOrWhiteSpace(ttlValue))
        {
            return false;
        }

        if (!double.TryParse(ttlValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if (seconds <= 0)
        {
            return false;
        }

        ttl = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
