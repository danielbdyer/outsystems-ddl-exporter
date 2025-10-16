using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Evidence;

public sealed class EvidenceCacheService : IEvidenceCacheService
{
    private const string ManifestFileName = "manifest.json";
    private const string ManifestVersion = "1.0";

    private readonly IFileSystem _fileSystem;
    private readonly Func<DateTimeOffset> _timestampProvider;
    private readonly EvidenceDescriptorCollector _descriptorCollector;
    private readonly ManifestEvaluator _manifestEvaluator;
    private readonly EvidenceCacheWriter _cacheWriter;

    public EvidenceCacheService(
        IFileSystem? fileSystem = null,
        Func<DateTimeOffset>? timestampProvider = null)
    {
        _fileSystem = fileSystem ?? new FileSystem();
        _timestampProvider = timestampProvider ?? (() => DateTimeOffset.UtcNow);
        _descriptorCollector = new EvidenceDescriptorCollector(_fileSystem);
        _manifestEvaluator = new ManifestEvaluator(_fileSystem);
        _cacheWriter = new EvidenceCacheWriter(_fileSystem);
    }

    public async Task<Result<EvidenceCacheResult>> CacheAsync(
        EvidenceCacheRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

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

        var metadata = request.Metadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(request.Metadata, StringComparer.Ordinal);

        var descriptorsResult = await _descriptorCollector.CollectAsync(request, cancellationToken).ConfigureAwait(false);
        if (descriptorsResult.IsFailure)
        {
            return Result<EvidenceCacheResult>.Failure(descriptorsResult.Errors);
        }

        var descriptors = descriptorsResult.Value;
        if (descriptors.Count == 0)
        {
            return ValidationError.Create("cache.artifacts.none", "At least one artifact must be provided to create a cache entry.");
        }

        var command = request.Command.Trim();
        var key = ComputeKey(command, descriptors, metadata);
        var cacheDirectory = _fileSystem.Path.Combine(normalizedRoot, key);
        var lockFilePath = string.Concat(cacheDirectory, ".lock");
        await using var cacheLock = await AcquireCacheLockAsync(lockFilePath, cancellationToken).ConfigureAwait(false);
        var requestedModuleSelection = BuildModuleSelection(metadata);

        EvidenceCacheInvalidationReason invalidationReason;
        IReadOnlyDictionary<string, string?> invalidationMetadata;

        if (_fileSystem.Directory.Exists(cacheDirectory))
        {
            if (request.Refresh)
            {
                invalidationReason = EvidenceCacheInvalidationReason.RefreshRequested;
                invalidationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.RefreshRequested)
                };

                _fileSystem.Directory.Delete(cacheDirectory, recursive: true);
            }
            else
            {
                var evaluationTimestamp = _timestampProvider();
                var evaluation = await _manifestEvaluator.EvaluateAsync(
                    cacheDirectory,
                    ManifestFileName,
                    ManifestVersion,
                    key,
                    command,
                    metadata,
                    requestedModuleSelection,
                    descriptors,
                    evaluationTimestamp,
                    cancellationToken).ConfigureAwait(false);

                if (evaluation.Outcome == EvidenceCacheOutcome.Reused && evaluation.Manifest is not null)
                {
                    var reuseMetadata = BuildOutcomeMetadata(
                        evaluationTimestamp,
                        evaluation.Manifest,
                        requestedModuleSelection,
                        evaluation.Metadata,
                        reuse: true,
                        EvidenceCacheInvalidationReason.None);

                    var reuseEvaluation = new EvidenceCacheEvaluation(
                        EvidenceCacheOutcome.Reused,
                        EvidenceCacheInvalidationReason.None,
                        evaluationTimestamp,
                        reuseMetadata);

                    return Result<EvidenceCacheResult>.Success(
                        new EvidenceCacheResult(cacheDirectory, evaluation.Manifest, reuseEvaluation));
                }

                invalidationReason = evaluation.Reason;
                invalidationMetadata = evaluation.Metadata;

                if (_fileSystem.Directory.Exists(cacheDirectory))
                {
                    _fileSystem.Directory.Delete(cacheDirectory, recursive: true);
                }
            }
        }
        else
        {
            (invalidationReason, invalidationMetadata) = await DetermineMissingCacheReasonAsync(
                normalizedRoot,
                command,
                metadata,
                descriptors,
                cancellationToken).ConfigureAwait(false);
        }

        var creationTimestamp = _timestampProvider();
        var manifest = await _cacheWriter.WriteAsync(
            cacheDirectory,
            ManifestFileName,
            ManifestVersion,
            key,
            command,
            creationTimestamp,
            requestedModuleSelection,
            metadata,
            descriptors,
            cancellationToken).ConfigureAwait(false);

        var creationMetadata = BuildOutcomeMetadata(
            creationTimestamp,
            manifest,
            requestedModuleSelection,
            invalidationMetadata,
            reuse: false,
            invalidationReason);

        var creationEvaluation = new EvidenceCacheEvaluation(
            EvidenceCacheOutcome.Created,
            invalidationReason,
            creationTimestamp,
            creationMetadata);

        return Result<EvidenceCacheResult>.Success(new EvidenceCacheResult(cacheDirectory, manifest, creationEvaluation));
    }

    private async ValueTask<CacheDirectoryLock> AcquireCacheLockAsync(string lockFilePath, CancellationToken cancellationToken)
    {
        var directory = _fileSystem.Path.GetDirectoryName(lockFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var stream = _fileSystem.File.Open(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new CacheDirectoryLock(_fileSystem, lockFilePath, stream);
            }
            catch (IOException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<(EvidenceCacheInvalidationReason Reason, IReadOnlyDictionary<string, string?> Metadata)> DetermineMissingCacheReasonAsync(
        string rootDirectory,
        string command,
        IReadOnlyDictionary<string, string?> metadata,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var directory in _fileSystem.Directory.EnumerateDirectories(rootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var manifestPath = _fileSystem.Path.Combine(directory, ManifestFileName);
                if (!_fileSystem.File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = await TryReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                if (manifest is null)
                {
                    continue;
                }

                if (!string.Equals(manifest.Command, command, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ArtifactsMatch(manifest, descriptors))
                {
                    continue;
                }

                if (!MetadataEquals(manifest.Metadata, metadata))
                {
                    var mismatchMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.MetadataMismatch),
                        ["manifest.metadataCount"] = manifest.Metadata.Count.ToString(CultureInfo.InvariantCulture),
                        ["request.metadataCount"] = metadata.Count.ToString(CultureInfo.InvariantCulture),
                        ["manifest.cacheKey"] = manifest.Key
                    };

                    return (EvidenceCacheInvalidationReason.MetadataMismatch, mismatchMetadata);
                }
            }
        }
        catch
        {
            // Ignore discovery failures and fall back to manifest missing semantics.
        }

        var defaultMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ManifestMissing)
        };

        return (EvidenceCacheInvalidationReason.ManifestMissing, defaultMetadata);
    }

    private async Task<EvidenceCacheManifest?> TryReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = _fileSystem.File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<EvidenceCacheManifest>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ArtifactsMatch(EvidenceCacheManifest manifest, IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors)
    {
        if (manifest.Artifacts.Count != descriptors.Count)
        {
            return false;
        }

        var descriptorsByType = descriptors.ToDictionary(static descriptor => descriptor.Type);
        foreach (var artifact in manifest.Artifacts)
        {
            if (!descriptorsByType.TryGetValue(artifact.Type, out var descriptor))
            {
                return false;
            }

            if (!string.Equals(artifact.Hash, descriptor.Hash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (artifact.Length != descriptor.Length)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value))
            {
                return false;
            }

            if (!string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

    private static IReadOnlyDictionary<string, string?> BuildOutcomeMetadata(
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

        return metadata;
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

    private sealed class CacheDirectoryLock : IAsyncDisposable
    {
        private readonly IFileSystem _fileSystem;
        private readonly string _lockFilePath;
        private readonly Stream _stream;
        private bool _disposed;

        public CacheDirectoryLock(IFileSystem fileSystem, string lockFilePath, Stream stream)
        {
            _fileSystem = fileSystem;
            _lockFilePath = lockFilePath;
            _stream = stream;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (_fileSystem.File.Exists(_lockFilePath))
                    {
                        _fileSystem.File.Delete(_lockFilePath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
