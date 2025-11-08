using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Evidence;

internal sealed class CacheRequestNormalizer
{
    private readonly IFileSystem _fileSystem;
    private readonly EvidenceDescriptorCollector _descriptorCollector;
    private readonly IPathCanonicalizer _pathCanonicalizer;

    public CacheRequestNormalizer(
        IFileSystem fileSystem,
        EvidenceDescriptorCollector descriptorCollector,
        IPathCanonicalizer pathCanonicalizer)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _descriptorCollector = descriptorCollector ?? throw new ArgumentNullException(nameof(descriptorCollector));
        _pathCanonicalizer = pathCanonicalizer ?? throw new ArgumentNullException(nameof(pathCanonicalizer));
    }

    public async Task<Result<CacheRequestContext>> TryNormalizeAsync(
        EvidenceCacheRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RootDirectory))
        {
            return ValidationError.Create("cache.root.missing", "Cache root directory must be provided.");
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return ValidationError.Create("cache.command.missing", "Command context must be provided for cache entries.");
        }

        var normalizedRoot = _fileSystem.Path.GetFullPath(request.RootDirectory.Trim());
        _fileSystem.Directory.CreateDirectory(normalizedRoot);
        var canonicalRoot = _pathCanonicalizer.Canonicalize(normalizedRoot);

        var metadata = request.Metadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : request.Metadata.ToDictionary(
                static pair => pair.Key,
                pair => pair.Value is null ? null : pair.Value,
                StringComparer.Ordinal);

        foreach (var metadataKey in metadata.Keys.ToArray())
        {
            metadata[metadataKey] = _pathCanonicalizer.CanonicalizeOrNull(metadata[metadataKey]);
        }

        var descriptorsResult = await _descriptorCollector
            .CollectAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (descriptorsResult.IsFailure)
        {
            return Result<CacheRequestContext>.Failure(descriptorsResult.Errors);
        }

        var descriptors = descriptorsResult.Value;
        if (descriptors.Count == 0)
        {
            return ValidationError.Create(
                "cache.artifacts.none",
                "At least one artifact must be provided to create a cache entry.");
        }

        var command = request.Command.Trim();
        var moduleSelection = BuildModuleSelection(metadata);
        var key = ComputeKey(command, descriptors, metadata);
        var cacheDirectory = _fileSystem.Path.Combine(normalizedRoot, key);
        var canonicalCacheDirectory = _pathCanonicalizer.Canonicalize(cacheDirectory);

        return new CacheRequestContext(
            canonicalRoot,
            canonicalCacheDirectory,
            command,
            key,
            metadata,
            descriptors,
            moduleSelection,
            request.Refresh);
    }

    private static EvidenceCacheModuleSelection BuildModuleSelection(IReadOnlyDictionary<string, string?> metadata)
    {
        var includeSystemModules = GetBoolean(metadata, "moduleFilter.includeSystemModules", defaultValue: true);
        var includeInactiveModules = GetBoolean(metadata, "moduleFilter.includeInactiveModules", defaultValue: true);

        IReadOnlyList<string> modules = Array.Empty<string>();
        if (metadata.TryGetValue("moduleFilter.modules", out var modulesValue) && !string.IsNullOrWhiteSpace(modulesValue))
        {
            modules = modulesValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(static module => module, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var moduleCount = modules.Count;
        if (metadata.TryGetValue("moduleFilter.moduleCount", out var moduleCountValue)
            && int.TryParse(moduleCountValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount)
            && parsedCount >= 0)
        {
            moduleCount = parsedCount;
        }

        metadata.TryGetValue("moduleFilter.modulesHash", out var modulesHash);

        return new EvidenceCacheModuleSelection(
            includeSystemModules,
            includeInactiveModules,
            moduleCount,
            modulesHash,
            modules);
    }

    private static string ComputeKey(
        string command,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        IReadOnlyDictionary<string, string?> metadata)
    {
        var builder = new StringBuilder();
        builder.Append("command=").Append(command).Append(';');

        foreach (var descriptor in descriptors.OrderBy(static d => d.Type))
        {
            builder
                .Append(descriptor.Type).Append(':')
                .Append(descriptor.Hash).Append(':')
                .Append(descriptor.Length.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        if (metadata.Count > 0)
        {
            foreach (var entry in metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append(entry.Key).Append('=');
                if (!string.IsNullOrEmpty(entry.Value))
                {
                    builder.Append(entry.Value);
                }

                builder.Append(';');
            }
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        var key = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return key.Substring(0, 16);
    }

    private static bool GetBoolean(IReadOnlyDictionary<string, string?> metadata, string key, bool defaultValue)
    {
        if (metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }
}
