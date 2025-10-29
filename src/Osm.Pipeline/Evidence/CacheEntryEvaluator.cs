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

internal sealed class CacheEntryEvaluator
{
    private readonly IFileSystem _fileSystem;
    private readonly ManifestEvaluator _manifestEvaluator;
    private readonly Func<DateTimeOffset> _timestampProvider;
    private readonly CacheMetadataBuilder _metadataBuilder;

    public CacheEntryEvaluator(
        IFileSystem fileSystem,
        ManifestEvaluator manifestEvaluator,
        Func<DateTimeOffset> timestampProvider,
        CacheMetadataBuilder metadataBuilder)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _manifestEvaluator = manifestEvaluator ?? throw new ArgumentNullException(nameof(manifestEvaluator));
        _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        _metadataBuilder = metadataBuilder ?? throw new ArgumentNullException(nameof(metadataBuilder));
    }

    public async Task<CacheEvaluationResult> EvaluateExistingEntryAsync(
        CacheRequestContext context,
        string manifestFileName,
        string manifestVersion,
        CancellationToken cancellationToken)
    {
        if (context.Refresh)
        {
            var refreshMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.RefreshRequested)
            };

            if (_fileSystem.Directory.Exists(context.CacheDirectory))
            {
                _fileSystem.Directory.Delete(context.CacheDirectory, recursive: true);
            }

            return CacheEvaluationResult.CreateInvalidation(
                EvidenceCacheInvalidationReason.RefreshRequested,
                refreshMetadata);
        }

        var evaluationTimestamp = _timestampProvider();
        var evaluation = await _manifestEvaluator.EvaluateAsync(
            context.CacheDirectory,
            manifestFileName,
            manifestVersion,
            context.Key,
            context.Command,
            context.Metadata,
            context.ModuleSelection,
            context.Descriptors,
            evaluationTimestamp,
            cancellationToken).ConfigureAwait(false);

        if (evaluation.Outcome == EvidenceCacheOutcome.Reused && evaluation.Manifest is not null)
        {
            var reuseMetadata = _metadataBuilder.BuildOutcomeMetadata(
                evaluationTimestamp,
                evaluation.Manifest,
                context.ModuleSelection,
                evaluation.Metadata,
                reuse: true,
                EvidenceCacheInvalidationReason.None);

            var reuseEvaluation = new EvidenceCacheEvaluation(
                EvidenceCacheOutcome.Reused,
                EvidenceCacheInvalidationReason.None,
                evaluationTimestamp,
                reuseMetadata);

            var reuseResult = new EvidenceCacheResult(context.CacheDirectory, evaluation.Manifest, reuseEvaluation);
            return CacheEvaluationResult.CreateReuse(reuseResult);
        }

        if (_fileSystem.Directory.Exists(context.CacheDirectory))
        {
            _fileSystem.Directory.Delete(context.CacheDirectory, recursive: true);
        }

        return CacheEvaluationResult.CreateInvalidation(evaluation.Reason, evaluation.Metadata);
    }

    public async Task<(EvidenceCacheInvalidationReason Reason, IReadOnlyDictionary<string, string?> Metadata)> DetermineMissingCacheReasonAsync(
        CacheRequestContext context,
        string manifestFileName,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var directory in _fileSystem.Directory.EnumerateDirectories(context.NormalizedRootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var manifestPath = _fileSystem.Path.Combine(directory, manifestFileName);
                if (!_fileSystem.File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = await TryReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                if (manifest is null)
                {
                    continue;
                }

                if (!string.Equals(manifest.Command, context.Command, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ArtifactsMatch(manifest, context.Descriptors))
                {
                    continue;
                }

                if (!MetadataEquals(manifest.Metadata, context.Metadata))
                {
                    var mismatchMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.MetadataMismatch),
                        ["manifest.metadataCount"] = manifest.Metadata.Count.ToString(CultureInfo.InvariantCulture),
                        ["request.metadataCount"] = context.Metadata.Count.ToString(CultureInfo.InvariantCulture),
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

    private static bool ArtifactsMatch(
        EvidenceCacheManifest manifest,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors)
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
}
