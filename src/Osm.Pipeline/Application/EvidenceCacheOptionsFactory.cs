using System;
using System.Collections.Generic;
using System.Globalization;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Application;

internal static class EvidenceCacheOptionsFactory
{
    public static EvidenceCachePipelineOptions? Create(
        string command,
        CliConfiguration configuration,
        TighteningOptions tightening,
        ModuleFilterOptions moduleFilter,
        string modelPath,
        string? profilePath,
        string? dmmPath,
        CacheOptionsOverrides overrides,
        string? configPath)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command name must be provided.", nameof(command));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (moduleFilter is null)
        {
            throw new ArgumentNullException(nameof(moduleFilter));
        }

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path must be provided.", nameof(modelPath));
        }

        overrides ??= new CacheOptionsOverrides(null, null, null, null);

        var cacheRoot = overrides.Root ?? configuration.Cache.Root;
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            return null;
        }

        var refresh = overrides.Refresh ?? configuration.Cache.Refresh ?? false;
        var maxAge = overrides.MaxAge ?? configuration.Cache.MaxAge;
        var maxEntries = overrides.MaxEntries ?? configuration.Cache.MaxEntries;
        var metadata = new Dictionary<string, string?>(
            CacheMetadataBuilder.Build(
                tightening,
                moduleFilter,
                configuration,
                modelPath,
                profilePath,
                dmmPath),
            StringComparer.Ordinal);

        if (maxAge is { } ttl)
        {
            metadata["cache.ttlSeconds"] = ttl.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        }

        if (maxEntries.HasValue)
        {
            metadata["cache.maxEntries"] = maxEntries.Value.ToString(CultureInfo.InvariantCulture);
        }

        return new EvidenceCachePipelineOptions(
            cacheRoot.Trim(),
            refresh,
            command,
            modelPath,
            profilePath,
            dmmPath,
            configPath,
            metadata,
            maxAge,
            maxEntries);
    }
}
