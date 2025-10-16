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
    private const string ManifestVersion = "1.1";

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

        var now = _timestampProvider();
        var timeToLive = ResolveTimeToLive(request, metadata);
        var key = ComputeKey(request.Command.Trim(), descriptors, metadata);
        var cacheDirectory = _fileSystem.Path.Combine(normalizedRoot, key);
        var moduleSelection = BuildModuleSelection(metadata);

        EvidenceCacheDecisionKind decision = EvidenceCacheDecisionKind.Created;
        var decisionMetadata = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (_fileSystem.Directory.Exists(cacheDirectory))
        {
            if (request.Refresh)
            {
                decision = EvidenceCacheDecisionKind.Refreshed;
                decisionMetadata["reason.refreshRequested"] = bool.TrueString;
                _fileSystem.Directory.Delete(cacheDirectory, recursive: true);
            }
            else
            {
                var manifestPath = _fileSystem.Path.Combine(cacheDirectory, ManifestFileName);
                var existingManifest = await TryReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);

                if (existingManifest is null)
                {
                    decision = EvidenceCacheDecisionKind.Refreshed;
                    decisionMetadata["reason.manifest.unreadable"] = bool.TrueString;
                    _fileSystem.Directory.Delete(cacheDirectory, recursive: true);
                }
                else
                {
                    var evaluation = EvaluateExistingManifest(existingManifest, metadata, moduleSelection, timeToLive, now);
                    if (evaluation.ShouldReuse)
                    {
                        var execution = CreateExecutionMetadata(
                            EvidenceCacheDecisionKind.Reused,
                            evaluation.Metadata,
                            existingManifest,
                            now);

                        return Result<EvidenceCacheResult>.Success(new EvidenceCacheResult(
                            cacheDirectory,
                            existingManifest,
                            execution));
                    }

                    decision = EvidenceCacheDecisionKind.Refreshed;
                    decisionMetadata = evaluation.Metadata;
                    _fileSystem.Directory.Delete(cacheDirectory, recursive: true);
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

        var createdAt = now;
        DateTimeOffset? expiresAt = timeToLive.HasValue ? createdAt.Add(timeToLive.Value) : null;
        var manifest = new EvidenceCacheManifest(
            ManifestVersion,
            key,
            request.Command.Trim(),
            createdAt,
            expiresAt,
            moduleSelection,
            metadata,
            artifacts);

        var manifestPathNew = _fileSystem.Path.Combine(cacheDirectory, ManifestFileName);
        await WriteManifestAsync(manifestPathNew, manifest, cancellationToken).ConfigureAwait(false);

        var executionMetadata = CreateExecutionMetadata(decision, decisionMetadata, manifest, now);
        return Result<EvidenceCacheResult>.Success(new EvidenceCacheResult(cacheDirectory, manifest, executionMetadata));
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

    private static TimeSpan? ResolveTimeToLive(EvidenceCacheRequest request, IReadOnlyDictionary<string, string?> metadata)
    {
        if (request.TimeToLive.HasValue)
        {
            return request.TimeToLive;
        }

        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        if (metadata.TryGetValue("cache.ttlSeconds", out var secondsValue)
            && double.TryParse(secondsValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (metadata.TryGetValue("cache.ttlMinutes", out var minutesValue)
            && double.TryParse(minutesValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var minutes)
            && minutes > 0)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        return null;
    }

    private static EvidenceCacheModuleSelection BuildModuleSelection(IReadOnlyDictionary<string, string?> metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new EvidenceCacheModuleSelection(null, Array.Empty<string>());
        }

        metadata.TryGetValue("moduleFilter.modules.hash", out var hash);

        if (!metadata.TryGetValue("moduleFilter.modules.normalized", out var normalized)
            || string.IsNullOrWhiteSpace(normalized))
        {
            return new EvidenceCacheModuleSelection(hash, Array.Empty<string>());
        }

        var modules = normalized
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static module => module.Trim())
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .ToArray();

        return new EvidenceCacheModuleSelection(hash, modules);
    }

    private CacheEvaluation EvaluateExistingManifest(
        EvidenceCacheManifest manifest,
        IReadOnlyDictionary<string, string?> metadata,
        EvidenceCacheModuleSelection requestedSelection,
        TimeSpan? requestedTimeToLive,
        DateTimeOffset now)
    {
        var evaluationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["manifest.version"] = manifest.Version,
            ["manifest.createdAtUtc"] = manifest.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        };

        if (manifest.ExpiresAtUtc is { } expiresAt)
        {
            evaluationMetadata["manifest.expiresAtUtc"] = expiresAt.ToString("O", CultureInfo.InvariantCulture);
        }

        if (manifest.ModuleSelection is { } existingSelection)
        {
            evaluationMetadata["manifest.moduleSelection.hash"] = existingSelection.Hash;
            evaluationMetadata["manifest.moduleSelection.count"] = existingSelection.Modules.Count
                .ToString(CultureInfo.InvariantCulture);
        }

        var metadataMatch = AreMetadataEquivalent(metadata, manifest.Metadata);
        if (!metadataMatch)
        {
            evaluationMetadata["reason.metadataMismatch"] = bool.TrueString;
        }

        var moduleMatch = AreModuleSelectionsEquivalent(requestedSelection, manifest.ModuleSelection);
        if (!moduleMatch)
        {
            evaluationMetadata["reason.moduleSelectionChanged"] = bool.TrueString;
            evaluationMetadata["requested.moduleSelection.hash"] = requestedSelection.Hash;
            evaluationMetadata["requested.moduleSelection.count"] = requestedSelection.Modules.Count
                .ToString(CultureInfo.InvariantCulture);
        }

        var expired = manifest.ExpiresAtUtc is { } expiration && expiration <= now;
        if (expired)
        {
            evaluationMetadata["reason.ttlExpired"] = bool.TrueString;
            evaluationMetadata["nowUtc"] = now.ToString("O", CultureInfo.InvariantCulture);
        }

        DateTimeOffset? expectedExpiry = requestedTimeToLive.HasValue
            ? manifest.CreatedAtUtc.Add(requestedTimeToLive.Value)
            : null;

        var ttlMismatch = false;
        if (requestedTimeToLive.HasValue)
        {
            if (!manifest.ExpiresAtUtc.HasValue || manifest.ExpiresAtUtc != expectedExpiry)
            {
                ttlMismatch = true;
                evaluationMetadata["reason.ttlChanged"] = bool.TrueString;
                evaluationMetadata["requested.ttlSeconds"] = requestedTimeToLive.Value.TotalSeconds
                    .ToString(CultureInfo.InvariantCulture);
            }
        }

        var versionMismatch = !string.Equals(manifest.Version, ManifestVersion, StringComparison.Ordinal);
        if (versionMismatch)
        {
            evaluationMetadata["reason.versionMismatch"] = bool.TrueString;
        }

        var shouldReuse = metadataMatch && moduleMatch && !expired && !ttlMismatch && !versionMismatch;

        return new CacheEvaluation(shouldReuse, evaluationMetadata);
    }

    private async Task<EvidenceCacheManifest?> TryReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = _fileSystem.File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<EvidenceCacheManifest>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private EvidenceCacheExecutionMetadata CreateExecutionMetadata(
        EvidenceCacheDecisionKind decision,
        IReadOnlyDictionary<string, string?>? decisionMetadata,
        EvidenceCacheManifest manifest,
        DateTimeOffset now)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["status"] = decision.ToString(),
            ["manifest.version"] = manifest.Version,
            ["manifest.createdAtUtc"] = manifest.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["timestamp.nowUtc"] = now.ToString("O", CultureInfo.InvariantCulture),
        };

        if (manifest.ExpiresAtUtc is { } expiresAt)
        {
            metadata["manifest.expiresAtUtc"] = expiresAt.ToString("O", CultureInfo.InvariantCulture);
        }

        if (manifest.ModuleSelection is { } selection)
        {
            metadata["moduleSelection.hash"] = selection.Hash;
            metadata["moduleSelection.count"] = selection.Modules.Count.ToString(CultureInfo.InvariantCulture);
        }

        if (decisionMetadata is not null)
        {
            foreach (var pair in decisionMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return new EvidenceCacheExecutionMetadata(decision, metadata);
    }

    private static bool AreMetadataEquivalent(
        IReadOnlyDictionary<string, string?> requested,
        IReadOnlyDictionary<string, string?>? existing)
    {
        if (requested is null)
        {
            return existing is null || existing.Count == 0;
        }

        if (existing is null)
        {
            return requested.Count == 0;
        }

        if (requested.Count != existing.Count)
        {
            return false;
        }

        foreach (var pair in requested)
        {
            if (!existing.TryGetValue(pair.Key, out var value))
            {
                return false;
            }

            if (!string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                var leftEmpty = string.IsNullOrEmpty(pair.Value);
                var rightEmpty = string.IsNullOrEmpty(value);
                if (!(leftEmpty && rightEmpty))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool AreModuleSelectionsEquivalent(
        EvidenceCacheModuleSelection requested,
        EvidenceCacheModuleSelection? existing)
    {
        if (existing is null)
        {
            return requested.Modules.Count == 0;
        }

        var requestedCount = requested.Modules.Count;
        var existingCount = existing.Modules.Count;
        if (requestedCount != existingCount)
        {
            return false;
        }

        for (var index = 0; index < requestedCount; index++)
        {
            var requestedValue = requested.Modules[index];
            var existingValue = existing.Modules[index];
            if (!string.Equals(requestedValue, existingValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(requested.Hash) || !string.IsNullOrEmpty(existing.Hash))
        {
            return string.Equals(requested.Hash, existing.Hash, StringComparison.Ordinal);
        }

        return true;
    }

    private sealed record CacheEvaluation(bool ShouldReuse, Dictionary<string, string?> Metadata);

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
