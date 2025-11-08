using System;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Evidence;

public sealed class EvidenceCacheService : IEvidenceCacheService
{
    private const string ManifestFileName = "manifest.json";
    private const string ManifestVersion = "1.0";

    private readonly IFileSystem _fileSystem;
    private readonly CacheRequestNormalizer _normalizer;
    private readonly CacheEntryEvaluator _evaluator;
    private readonly CacheEntryCreator _creator;

    public EvidenceCacheService(
        IFileSystem? fileSystem = null,
        Func<DateTimeOffset>? timestampProvider = null,
        IPathCanonicalizer? pathCanonicalizer = null)
    {
        _fileSystem = fileSystem ?? new FileSystem();
        var timestamp = timestampProvider ?? (() => DateTimeOffset.UtcNow);
        var canonicalizer = pathCanonicalizer ?? new ForwardSlashPathCanonicalizer();

        var descriptorCollector = new EvidenceDescriptorCollector(_fileSystem, canonicalizer);
        var manifestEvaluator = new ManifestEvaluator(_fileSystem);
        var cacheWriter = new EvidenceCacheWriter(_fileSystem, canonicalizer);
        var metadataBuilder = new CacheMetadataBuilder(canonicalizer);

        _normalizer = new CacheRequestNormalizer(_fileSystem, descriptorCollector, canonicalizer);
        _evaluator = new CacheEntryEvaluator(_fileSystem, manifestEvaluator, timestamp, metadataBuilder);
        _creator = new CacheEntryCreator(cacheWriter, timestamp, metadataBuilder);
    }

    public async Task<Result<EvidenceCacheResult>> CacheAsync(
        EvidenceCacheRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var normalizationResult = await _normalizer.TryNormalizeAsync(request, cancellationToken).ConfigureAwait(false);
        if (normalizationResult.IsFailure)
        {
            return Result<EvidenceCacheResult>.Failure(normalizationResult.Errors);
        }

        var context = normalizationResult.Value;

        CacheEvaluationResult evaluationResult;
        if (_fileSystem.Directory.Exists(context.CacheDirectory))
        {
            evaluationResult = await _evaluator
                .EvaluateExistingEntryAsync(context, ManifestFileName, ManifestVersion, cancellationToken)
                .ConfigureAwait(false);

            if (evaluationResult is CacheEvaluationResult.Reuse reuse)
            {
                return Result<EvidenceCacheResult>.Success(reuse.Result);
            }
        }
        else
        {
            var missingCacheReason = await _evaluator
                .DetermineMissingCacheReasonAsync(context, ManifestFileName, cancellationToken)
                .ConfigureAwait(false);

            evaluationResult = CacheEvaluationResult.CreateInvalidation(
                missingCacheReason.Reason,
                missingCacheReason.Metadata);
        }

        if (evaluationResult is not CacheEvaluationResult.Invalidate invalidate)
        {
            throw new InvalidOperationException("Expected invalidation metadata when cache reuse did not occur.");
        }

        var created = await _creator
            .CreateAsync(
                context,
                ManifestFileName,
                ManifestVersion,
                invalidate.Reason,
                invalidate.Metadata,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<EvidenceCacheResult>.Success(created);
    }
}
