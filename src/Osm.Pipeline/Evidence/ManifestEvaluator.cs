using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Evidence;

internal sealed class ManifestEvaluator
{
    private readonly IFileSystem _fileSystem;

    public ManifestEvaluator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<ManifestEvaluation> EvaluateAsync(
        string cacheDirectory,
        string manifestFileName,
        string manifestVersion,
        string expectedKey,
        string command,
        IReadOnlyDictionary<string, string?> metadata,
        EvidenceCacheModuleSelection requestedSelection,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        DateTimeOffset evaluationTimestamp,
        CancellationToken cancellationToken)
    {
        var manifestPath = _fileSystem.Path.Combine(cacheDirectory, manifestFileName);
        var evaluationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["evaluatedAtUtc"] = evaluationTimestamp.ToString("O", CultureInfo.InvariantCulture)
        };

        if (!_fileSystem.File.Exists(manifestPath))
        {
            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ManifestMissing);
            return new ManifestEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestMissing,
                null,
                evaluationMetadata);
        }

        var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ManifestInvalid);
            return new ManifestEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestInvalid,
                null,
                evaluationMetadata);
        }

        evaluationMetadata["manifest.version"] = manifest.Version;

        if (!string.Equals(manifest.Version, manifestVersion, StringComparison.Ordinal))
        {
            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ManifestVersionMismatch);
            evaluationMetadata["expectedVersion"] = manifestVersion;
            evaluationMetadata["actualVersion"] = manifest.Version;
            return new ManifestEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestVersionMismatch,
                manifest,
                evaluationMetadata);
        }

        if (!string.Equals(manifest.Key, expectedKey, StringComparison.Ordinal))
        {
            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.KeyMismatch);
            evaluationMetadata["expectedKey"] = expectedKey;
            evaluationMetadata["actualKey"] = manifest.Key;
            return new ManifestEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.KeyMismatch,
                manifest,
                evaluationMetadata);
        }

        if (!string.Equals(manifest.Command, command, StringComparison.Ordinal))
        {
            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.CommandMismatch);
            evaluationMetadata["expectedCommand"] = command;
            evaluationMetadata["actualCommand"] = manifest.Command;
            return new ManifestEvaluation(
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

            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ManifestExpired);
            return new ManifestEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ManifestExpired,
                manifest,
                evaluationMetadata);
        }

        var manifestSelection = manifest.ModuleSelection ?? EvidenceCacheModuleSelection.Empty;
        if (!ModuleSelectionEquals(manifestSelection, requestedSelection))
        {
            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ModuleSelectionChanged);
            evaluationMetadata["expected.selection.hash"] = requestedSelection.ModulesHash;
            evaluationMetadata["actual.selection.hash"] = manifestSelection.ModulesHash;
            evaluationMetadata["expected.selection.count"] = requestedSelection.ModuleCount.ToString(CultureInfo.InvariantCulture);
            evaluationMetadata["actual.selection.count"] = manifestSelection.ModuleCount.ToString(CultureInfo.InvariantCulture);
            return new ManifestEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ModuleSelectionChanged,
                manifest,
                evaluationMetadata);
        }

        if (!MetadataEquals(manifest.Metadata, metadata))
        {
            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.MetadataMismatch);
            evaluationMetadata["manifest.metadataCount"] = manifest.Metadata.Count.ToString(CultureInfo.InvariantCulture);
            evaluationMetadata["request.metadataCount"] = metadata.Count.ToString(CultureInfo.InvariantCulture);
            return new ManifestEvaluation(
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

            evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ArtifactsMismatch);
            return new ManifestEvaluation(
                EvidenceCacheOutcome.Created,
                EvidenceCacheInvalidationReason.ArtifactsMismatch,
                manifest,
                evaluationMetadata);
        }

        var normalizedManifest = manifest with { LastValidatedAtUtc = evaluationTimestamp };
        evaluationMetadata["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.None);
        evaluationMetadata["manifest.lastValidatedAtUtc"] = evaluationTimestamp.ToString("O", CultureInfo.InvariantCulture);
        return new ManifestEvaluation(
            EvidenceCacheOutcome.Reused,
            EvidenceCacheInvalidationReason.None,
            normalizedManifest,
            evaluationMetadata);
    }

    private async Task<EvidenceCacheManifest?> ReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
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

    private static bool HasExpired(
        EvidenceCacheManifest manifest,
        DateTimeOffset evaluationTimestamp,
        out Dictionary<string, string?> metadata)
    {
        metadata = new Dictionary<string, string?>(StringComparer.Ordinal);

        var expiresAt = manifest.ExpiresAtUtc;
        if (expiresAt is null || expiresAt == DateTimeOffset.MinValue)
        {
            if (EvidenceCacheTtlPolicy.TryGetTtl(manifest.Metadata, out var ttl))
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
}

internal sealed record ManifestEvaluation(
    EvidenceCacheOutcome Outcome,
    EvidenceCacheInvalidationReason Reason,
    EvidenceCacheManifest? Manifest,
    IReadOnlyDictionary<string, string?> Metadata);
