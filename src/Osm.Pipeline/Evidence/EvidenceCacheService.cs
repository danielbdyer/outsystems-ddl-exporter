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

        var descriptorsResult = await CollectDescriptorsAsync(request, cancellationToken);
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

        if (_fileSystem.Directory.Exists(cacheDirectory))
        {
            if (request.Refresh)
            {
                _fileSystem.Directory.Delete(cacheDirectory, recursive: true);
            }
            else
            {
                var existingManifest = await TryReuseExistingCacheAsync(
                    cacheDirectory,
                    key,
                    command,
                    metadata,
                    descriptors,
                    cancellationToken).ConfigureAwait(false);

                if (existingManifest is not null)
                {
                    return Result<EvidenceCacheResult>.Success(new EvidenceCacheResult(cacheDirectory, existingManifest));
                }
            }
        }

        _fileSystem.Directory.CreateDirectory(cacheDirectory);

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
            _timestampProvider(),
            metadata,
            artifacts);

        var manifestPath = _fileSystem.Path.Combine(cacheDirectory, ManifestFileName);
        await WriteManifestAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);

        return Result<EvidenceCacheResult>.Success(new EvidenceCacheResult(cacheDirectory, manifest));
    }

    private async Task<EvidenceCacheManifest?> TryReuseExistingCacheAsync(
        string cacheDirectory,
        string expectedKey,
        string command,
        IReadOnlyDictionary<string, string?> metadata,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        CancellationToken cancellationToken)
    {
        var manifestPath = _fileSystem.Path.Combine(cacheDirectory, ManifestFileName);
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            return null;
        }

        if (!ManifestMatches(manifest, expectedKey, command, metadata, descriptors, cacheDirectory))
        {
            return null;
        }

        return manifest;
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

    private bool ManifestMatches(
        EvidenceCacheManifest manifest,
        string expectedKey,
        string command,
        IReadOnlyDictionary<string, string?> metadata,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        string cacheDirectory)
    {
        if (!string.Equals(manifest.Version, ManifestVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(manifest.Key, expectedKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(manifest.Command, command, StringComparison.Ordinal))
        {
            return false;
        }

        if (!MetadataEquals(manifest.Metadata, metadata))
        {
            return false;
        }

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

            if (string.IsNullOrWhiteSpace(artifact.RelativePath))
            {
                return false;
            }

            var artifactPath = _fileSystem.Path.Combine(cacheDirectory, artifact.RelativePath);
            if (!_fileSystem.File.Exists(artifactPath))
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

    private sealed record EvidenceArtifactDescriptor(
        EvidenceArtifactType Type,
        string SourcePath,
        string Hash,
        long Length,
        string Extension);
}
