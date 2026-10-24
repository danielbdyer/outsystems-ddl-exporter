using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Osm.Pipeline.Evidence;

internal sealed class CacheMetadataBuilder
{
    private readonly IPathCanonicalizer _pathCanonicalizer;

    public CacheMetadataBuilder(IPathCanonicalizer pathCanonicalizer)
    {
        _pathCanonicalizer = pathCanonicalizer ?? throw new ArgumentNullException(nameof(pathCanonicalizer));
    }

    public IReadOnlyDictionary<string, string?> BuildOutcomeMetadata(
        DateTimeOffset evaluatedAtUtc,
        EvidenceCacheManifest manifest,
        EvidenceCacheModuleSelection moduleSelection,
        IReadOnlyDictionary<string, string?> baseMetadata,
        bool reuse,
        EvidenceCacheInvalidationReason reason)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["evaluatedAtUtc"] = evaluatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["cacheKey"] = manifest.Key,
            ["action"] = reuse ? "reused" : "created",
            ["manifest.createdAtUtc"] = manifest.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["moduleSelection.count"] = moduleSelection.ModuleCount.ToString(CultureInfo.InvariantCulture),
            ["moduleSelection.includeSystemModules"] = moduleSelection.IncludeSystemModules.ToString(),
            ["moduleSelection.includeInactiveModules"] = moduleSelection.IncludeInactiveModules.ToString(),
        };

        if (!string.IsNullOrEmpty(moduleSelection.ModulesHash))
        {
            metadata["moduleSelection.hash"] = moduleSelection.ModulesHash;
        }

        if (manifest.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc != DateTimeOffset.MinValue)
        {
            metadata["manifest.expiresAtUtc"] = expiresAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        foreach (var pair in baseMetadata)
        {
            metadata[pair.Key] = pair.Value;
        }

        if (!metadata.ContainsKey("reason"))
        {
            metadata["reason"] = EvidenceCacheReasonMapper.Map(reason);
        }

        foreach (var key in metadata.Keys.ToArray())
        {
            metadata[key] = _pathCanonicalizer.CanonicalizeOrNull(metadata[key]);
        }

        return metadata;
    }
}
