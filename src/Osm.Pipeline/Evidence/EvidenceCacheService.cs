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

    public EvidenceCacheService(IFileSystem? fileSystem = null, Func<DateTimeOffset>? timestampProvider = null)
    {
        _fileSystem = fileSystem ?? new FileSystem();
        _timestampProvider = timestampProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<Result<EvidenceCacheResult>> CacheAsync(EvidenceCacheRequest request, CancellationToken cancellationToken = default)
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

        var descriptorsResult = await CollectDescriptorsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    ["reason"] = MapReason(EvidenceCacheInvalidationReason.RefreshRequested)
                };

                _fileSystem.Directory.Delete(cacheDirectory, recursive: true);
            }
            else
            {
                var evaluationTimestamp = _timestampProvider();
                var evaluation = await EvaluateExistingCacheAsync(
                    cacheDirectory,
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
            invalidationReason = EvidenceCacheInvalidationReason.ManifestMissing;
            invalidationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["reason"] = MapReason(EvidenceCacheInvalidationReason.ManifestMissing)
            };
        }

        _fileSystem.Directory.CreateDirectory(cacheDirectory);

        var creationTimestamp = _timestampProvider();
        var expiresAtUtc = DetermineExpiry(creationTimestamp, metadata);
        var artifacts = new List<EvidenceCacheArtifact>(descriptors.Count);

        foreach (var descriptor in descriptors.OrderBy(static d => d.Type))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = BuildArtifactFileName(descriptor);
            var destinationPath = _fileSystem.Path.Combine(cacheDirectory, relativePath);
            await CopyFileAsync(descriptor.SourcePath, destinationPath, cancellationToken).ConfigureAwait(false);

            artifacts.Add(new EvidenceCacheArtifact(
                descriptor.Type,
                descriptor.SourcePath,
                relativePath,
                descriptor.Hash,
                descriptor.Length));
        }

        var manifest = new EvidenceCacheManifest(
            ManifestVersion,
            key,
            command,
            creationTimestamp,
            creationTimestamp,
            expiresAtUtc,
            requestedModuleSelection,
            metadata,
            artifacts);

        var manifestPath = _fileSystem.Path.Combine(cacheDirectory, ManifestFileName);
        await WriteManifestAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);

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

    private async Task<ExistingCacheEvaluation> EvaluateExistingCacheAsync(
        string cacheDirectory,
        string expectedKey,
        string command,
        IReadOnlyDictionary<string, string?> metadata,
        EvidenceCacheModuleSelection requestedSelection,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        DateTimeOffset evaluationTimestamp,
        CancellationToken cancellationToken)
    {
        var manifestPath = _fileSystem.Path.Combine(cacheDirectory, ManifestFileName);
        var evaluationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["evaluatedAtUtc"] = evaluationTimestamp.ToString("O", CultureInfo.InvariantCulture)
        };

        if (!_fileSystem.File.Exists(manifestPath))
        {
            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.ManifestMissing);
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestMissing,
                null,
                evaluationMetadata);
        }

        var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.ManifestInvalid);
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestInvalid,
                null,
                evaluationMetadata);
        }

        evaluationMetadata["manifest.version"] = manifest.Version;

        if (!string.Equals(manifest.Version, ManifestVersion, StringComparison.Ordinal))
        {
            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.ManifestVersionMismatch);
            evaluationMetadata["expectedVersion"] = ManifestVersion;
            evaluationMetadata["actualVersion"] = manifest.Version;
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestVersionMismatch,
                manifest,
                evaluationMetadata);
        }

        if (!string.Equals(manifest.Key, expectedKey, StringComparison.Ordinal))
        {
            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.KeyMismatch);
            evaluationMetadata["expectedKey"] = expectedKey;
            evaluationMetadata["actualKey"] = manifest.Key;
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.KeyMismatch,
                manifest,
                evaluationMetadata);
        }

        if (!string.Equals(manifest.Command, command, StringComparison.Ordinal))
        {
            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.CommandMismatch);
            evaluationMetadata["expectedCommand"] = command;
            evaluationMetadata["actualCommand"] = manifest.Command;
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.CommandMismatch,
                manifest,
                evaluationMetadata);
        }

        if (HasExpired(manifest, evaluationTimestamp, out var expiryMetadata))
        {
            foreach (var pair in expiryMetadata)
            {
                evaluationMetadata[pair.Key] = pair.Value;
            }

            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.ManifestExpired);
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestExpired,
                manifest,
                evaluationMetadata);
        }

        var manifestSelection = manifest.ModuleSelection ?? EvidenceCacheModuleSelection.Empty;
        if (!ModuleSelectionEquals(manifestSelection, requestedSelection))
        {
            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.ModuleSelectionChanged);
            evaluationMetadata["expected.selection.hash"] = requestedSelection.ModulesHash;
            evaluationMetadata["actual.selection.hash"] = manifestSelection.ModulesHash;
            evaluationMetadata["expected.selection.count"] = requestedSelection.ModuleCount.ToString(CultureInfo.InvariantCulture);
            evaluationMetadata["actual.selection.count"] = manifestSelection.ModuleCount.ToString(CultureInfo.InvariantCulture);
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ModuleSelectionChanged,
                manifest,
                evaluationMetadata);
        }

        if (!MetadataEquals(manifest.Metadata, metadata))
        {
            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.MetadataMismatch);
            evaluationMetadata["manifest.metadataCount"] = manifest.Metadata.Count.ToString(CultureInfo.InvariantCulture);
            evaluationMetadata["request.metadataCount"] = metadata.Count.ToString(CultureInfo.InvariantCulture);
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.MetadataMismatch,
                manifest,
                evaluationMetadata);
        }

        if (!ArtifactsMatch(manifest, descriptors, cacheDirectory, out var artifactMetadata))
        {
            foreach (var pair in artifactMetadata)
            {
                evaluationMetadata[pair.Key] = pair.Value;
            }

            evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.ArtifactsMismatch);
            return new ExistingCacheEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ArtifactsMismatch,
                manifest,
                evaluationMetadata);
        }

        var normalizedManifest = manifest with { LastValidatedAtUtc = evaluationTimestamp };
        evaluationMetadata["reason"] = MapReason(EvidenceCacheInvalidationReason.None);
        evaluationMetadata["manifest.lastValidatedAtUtc"] = evaluationTimestamp.ToString("O", CultureInfo.InvariantCulture);
        return new ExistingCacheEvaluation(
            EvidenceCacheOutcome.Reused,
            EvidenceCacheInvalidationReason.None,
            normalizedManifest,
            evaluationMetadata);
    }

    private async Task<EvidenceCacheManifest?> ReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (!_fileSystem.File.Exists(manifestPath))
        {
            return null;
        }

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

    private static bool ModuleSelectionEquals(EvidenceCacheModuleSelection left, EvidenceCacheModuleSelection right)
    {
        if (left.IncludeSystemModules != right.IncludeSystemModules)
        {
            return false;
        }

        if (left.IncludeInactiveModules != right.IncludeInactiveModules)
        {
            return false;
        }

        if (left.ModuleCount != right.ModuleCount)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(left.ModulesHash) || !string.IsNullOrEmpty(right.ModulesHash))
        {
            return string.Equals(left.ModulesHash, right.ModulesHash, StringComparison.Ordinal);
        }

        if (left.Modules.Count != right.Modules.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Modules.Count; index++)
        {
            if (!string.Equals(left.Modules[index], right.Modules[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private bool ArtifactsMatch(
        EvidenceCacheManifest manifest,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        string cacheDirectory,
        out Dictionary<string, string?> metadata)
    {
        metadata = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (manifest.Artifacts.Count != descriptors.Count)
        {
            metadata["artifactMismatch.reason"] = "count";
            metadata["manifest.artifactCount"] = manifest.Artifacts.Count.ToString(CultureInfo.InvariantCulture);
            metadata["expected.artifactCount"] = descriptors.Count.ToString(CultureInfo.InvariantCulture);
            return false;
        }

        var descriptorsByType = descriptors.ToDictionary(static descriptor => descriptor.Type);

        foreach (var artifact in manifest.Artifacts)
        {
            if (!descriptorsByType.TryGetValue(artifact.Type, out var descriptor))
            {
                metadata["artifactMismatch.reason"] = "type";
                metadata["artifactMismatch.type"] = artifact.Type.ToString();
                return false;
            }

            if (!string.Equals(artifact.Hash, descriptor.Hash, StringComparison.OrdinalIgnoreCase))
            {
                metadata["artifactMismatch.reason"] = "hash";
                metadata["artifactMismatch.type"] = artifact.Type.ToString();
                metadata["expected.hash"] = descriptor.Hash;
                metadata["actual.hash"] = artifact.Hash;
                return false;
            }

            if (artifact.Length != descriptor.Length)
            {
                metadata["artifactMismatch.reason"] = "length";
                metadata["artifactMismatch.type"] = artifact.Type.ToString();
                metadata["expected.length"] = descriptor.Length.ToString(CultureInfo.InvariantCulture);
                metadata["actual.length"] = artifact.Length.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (string.IsNullOrWhiteSpace(artifact.RelativePath))
            {
                metadata["artifactMismatch.reason"] = "path.empty";
                metadata["artifactMismatch.type"] = artifact.Type.ToString();
                return false;
            }

            var artifactPath = _fileSystem.Path.Combine(cacheDirectory, artifact.RelativePath);
            if (!_fileSystem.File.Exists(artifactPath))
            {
                metadata["artifactMismatch.reason"] = "path.missing";
                metadata["artifactMismatch.type"] = artifact.Type.ToString();
                metadata["artifactMismatch.relativePath"] = artifact.RelativePath;
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

    private DateTimeOffset? DetermineExpiry(DateTimeOffset createdAtUtc, IReadOnlyDictionary<string, string?> metadata)
    {
        if (TryGetTtl(metadata, out var ttl))
        {
            return createdAtUtc.Add(ttl);
        }

        return null;
    }

    private static bool TryGetTtl(IReadOnlyDictionary<string, string?> metadata, out TimeSpan ttl)
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

    private bool HasExpired(
        EvidenceCacheManifest manifest,
        DateTimeOffset evaluationTimestamp,
        out Dictionary<string, string?> metadata)
    {
        metadata = new Dictionary<string, string?>(StringComparer.Ordinal);

        var expiresAt = manifest.ExpiresAtUtc;
        if (expiresAt is null || expiresAt == DateTimeOffset.MinValue)
        {
            if (TryGetTtl(manifest.Metadata, out var ttl))
            {
                expiresAt = manifest.CreatedAtUtc.Add(ttl);
            }
        }

        if (expiresAt is null)
        {
            return false;
        }

        if (evaluationTimestamp < expiresAt.Value)
        {
            return false;
        }

        metadata["expiresAtUtc"] = expiresAt.Value.ToString("O", CultureInfo.InvariantCulture);
        metadata["expiredAtUtc"] = evaluationTimestamp.ToString("O", CultureInfo.InvariantCulture);
        return true;
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
            metadata["reason"] = MapReason(reason);
        }

        return metadata;
    }

    private static string MapReason(EvidenceCacheInvalidationReason reason)
    {
        return reason switch
        {
            EvidenceCacheInvalidationReason.None => "cache.reused",
            EvidenceCacheInvalidationReason.ManifestMissing => "manifest.missing",
            EvidenceCacheInvalidationReason.ManifestInvalid => "manifest.invalid",
            EvidenceCacheInvalidationReason.ManifestVersionMismatch => "manifest.version.mismatch",
            EvidenceCacheInvalidationReason.KeyMismatch => "cache.key.mismatch",
            EvidenceCacheInvalidationReason.CommandMismatch => "cache.command.mismatch",
            EvidenceCacheInvalidationReason.ManifestExpired => "manifest.expired",
            EvidenceCacheInvalidationReason.ModuleSelectionChanged => "module.selection.changed",
            EvidenceCacheInvalidationReason.MetadataMismatch => "metadata.mismatch",
            EvidenceCacheInvalidationReason.ArtifactsMismatch => "artifacts.mismatch",
            EvidenceCacheInvalidationReason.RefreshRequested => "refresh.requested",
            _ => reason.ToString()
        };
    }

    private static bool GetBoolean(IReadOnlyDictionary<string, string?> metadata, string key, bool defaultValue)
    {
        if (metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
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

    private async Task<Result<List<EvidenceArtifactDescriptor>>> CollectDescriptorsAsync(EvidenceCacheRequest request, CancellationToken cancellationToken)
    {
        var descriptors = new List<EvidenceArtifactDescriptor>();

        var modelResult = await DescribeAsync(EvidenceArtifactType.Model, request.ModelPath, cancellationToken).ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<List<EvidenceArtifactDescriptor>>.Failure(modelResult.Errors);
        }

        if (modelResult.Value is not null)
        {
            descriptors.Add(modelResult.Value);
        }

        var profileResult = await DescribeAsync(EvidenceArtifactType.Profile, request.ProfilePath, cancellationToken).ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<List<EvidenceArtifactDescriptor>>.Failure(profileResult.Errors);
        }

        if (profileResult.Value is not null)
        {
            descriptors.Add(profileResult.Value);
        }

        var dmmResult = await DescribeAsync(EvidenceArtifactType.Dmm, request.DmmPath, cancellationToken).ConfigureAwait(false);
        if (dmmResult.IsFailure)
        {
            return Result<List<EvidenceArtifactDescriptor>>.Failure(dmmResult.Errors);
        }

        if (dmmResult.Value is not null)
        {
            descriptors.Add(dmmResult.Value);
        }

        var configResult = await DescribeAsync(EvidenceArtifactType.Configuration, request.ConfigPath, cancellationToken).ConfigureAwait(false);
        if (configResult.IsFailure)
        {
            return Result<List<EvidenceArtifactDescriptor>>.Failure(configResult.Errors);
        }

        if (configResult.Value is not null)
        {
            descriptors.Add(configResult.Value);
        }

        return descriptors;
    }

    private async Task<Result<EvidenceArtifactDescriptor?>> DescribeAsync(
        EvidenceArtifactType type,
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<EvidenceArtifactDescriptor?>.Success(null);
        }

        var trimmed = path.Trim();
        if (!_fileSystem.File.Exists(trimmed))
        {
            return ValidationError.Create(
                GetMissingCode(type),
                $"{GetArtifactLabel(type)} '{trimmed}' was not found.");
        }

        await using var stream = _fileSystem.File.Open(trimmed, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var length = stream.Length;
        var extension = _fileSystem.Path.GetExtension(trimmed);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = type switch
            {
                EvidenceArtifactType.Dmm => ".sql",
                _ => ".json",
            };
        }

        return new EvidenceArtifactDescriptor(type, trimmed, hash, length, extension);
    }

    private static string GetMissingCode(EvidenceArtifactType type)
    {
        return type switch
        {
            EvidenceArtifactType.Model => "cache.model.notFound",
            EvidenceArtifactType.Profile => "cache.profile.notFound",
            EvidenceArtifactType.Dmm => "cache.dmm.notFound",
            EvidenceArtifactType.Configuration => "cache.config.notFound",
            _ => "cache.artifact.notFound",
        };
    }

    private static string GetArtifactLabel(EvidenceArtifactType type)
    {
        return type switch
        {
            EvidenceArtifactType.Model => "Model",
            EvidenceArtifactType.Profile => "Profiling snapshot",
            EvidenceArtifactType.Dmm => "DMM script",
            EvidenceArtifactType.Configuration => "Configuration",
            _ => "Artifact",
        };
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

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        var key = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return key.Substring(0, 16);
    }

    private string BuildArtifactFileName(EvidenceArtifactDescriptor descriptor)
    {
        var prefix = descriptor.Type switch
        {
            EvidenceArtifactType.Model => "model",
            EvidenceArtifactType.Profile => "profile",
            EvidenceArtifactType.Dmm => "dmm",
            EvidenceArtifactType.Configuration => "config",
            _ => "artifact",
        };

        var extension = descriptor.Extension.StartsWith(".", StringComparison.Ordinal)
            ? descriptor.Extension
            : $".{descriptor.Extension}";

        return string.Concat(prefix, extension);
    }

    private async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var directory = _fileSystem.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        await using var source = _fileSystem.File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = _fileSystem.File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteManifestAsync(string manifestPath, EvidenceCacheManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = _fileSystem.File.Open(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
    }

    private sealed record ExistingCacheEvaluation(
        EvidenceCacheOutcome Outcome,
        EvidenceCacheInvalidationReason Reason,
        EvidenceCacheManifest? Manifest,
        IReadOnlyDictionary<string, string?> Metadata);

    private sealed record EvidenceArtifactDescriptor(
        EvidenceArtifactType Type,
        string SourcePath,
        string Hash,
        long Length,
        string Extension);
}
