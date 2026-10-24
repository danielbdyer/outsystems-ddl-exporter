using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Evidence;

internal sealed class CacheEntryCreator
{
    private readonly EvidenceCacheWriter _cacheWriter;
    private readonly Func<DateTimeOffset> _timestampProvider;
    private readonly CacheMetadataBuilder _metadataBuilder;

    public CacheEntryCreator(
        EvidenceCacheWriter cacheWriter,
        Func<DateTimeOffset> timestampProvider,
        CacheMetadataBuilder metadataBuilder)
    {
        _cacheWriter = cacheWriter ?? throw new ArgumentNullException(nameof(cacheWriter));
        _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        _metadataBuilder = metadataBuilder ?? throw new ArgumentNullException(nameof(metadataBuilder));
    }

    public async Task<EvidenceCacheResult> CreateAsync(
        CacheRequestContext context,
        string manifestFileName,
        string manifestVersion,
        EvidenceCacheInvalidationReason invalidationReason,
        IReadOnlyDictionary<string, string?> invalidationMetadata,
        CancellationToken cancellationToken)
    {
        var creationTimestamp = _timestampProvider();
        var manifest = await _cacheWriter.WriteAsync(
            context.CacheDirectory,
            manifestFileName,
            manifestVersion,
            context.Key,
            context.Command,
            creationTimestamp,
            context.ModuleSelection,
            context.Metadata,
            context.Descriptors,
            cancellationToken).ConfigureAwait(false);

        var creationMetadata = _metadataBuilder.BuildOutcomeMetadata(
            creationTimestamp,
            manifest,
            context.ModuleSelection,
            invalidationMetadata,
            reuse: false,
            invalidationReason);

        var creationEvaluation = new EvidenceCacheEvaluation(
            EvidenceCacheOutcome.Created,
            invalidationReason,
            creationTimestamp,
            creationMetadata);

        return new EvidenceCacheResult(context.CacheDirectory, manifest, creationEvaluation);
    }
}
