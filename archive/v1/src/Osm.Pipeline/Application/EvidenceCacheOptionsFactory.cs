using System;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Application;

internal sealed class EvidenceCacheOptionsFactory
{
    private readonly CacheMetadataBuilder _metadataBuilder;
    private readonly IPathCanonicalizer _pathCanonicalizer;

    public EvidenceCacheOptionsFactory(CacheMetadataBuilder metadataBuilder, IPathCanonicalizer pathCanonicalizer)
    {
        _metadataBuilder = metadataBuilder ?? throw new ArgumentNullException(nameof(metadataBuilder));
        _pathCanonicalizer = pathCanonicalizer ?? throw new ArgumentNullException(nameof(pathCanonicalizer));
    }

    public EvidenceCachePipelineOptions? Create(
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

        overrides ??= new CacheOptionsOverrides(null, null);

        var cacheRoot = overrides.Root ?? configuration.Cache.Root;
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            return null;
        }

        var refresh = overrides.Refresh ?? configuration.Cache.Refresh ?? false;

        var canonicalModelPath = _pathCanonicalizer.Canonicalize(modelPath);
        var canonicalProfilePath = _pathCanonicalizer.CanonicalizeOrNull(profilePath);
        var canonicalDmmPath = _pathCanonicalizer.CanonicalizeOrNull(dmmPath);
        var canonicalConfigPath = _pathCanonicalizer.CanonicalizeOrNull(configPath);

        var metadata = _metadataBuilder.Build(
            tightening,
            moduleFilter,
            configuration,
            canonicalModelPath,
            canonicalProfilePath,
            canonicalDmmPath);

        return new EvidenceCachePipelineOptions(
            _pathCanonicalizer.Canonicalize(cacheRoot),
            refresh,
            command.Trim(),
            canonicalModelPath,
            canonicalProfilePath,
            canonicalDmmPath,
            canonicalConfigPath,
            metadata);
    }
}
