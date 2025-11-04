using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class CacheEntryEvaluatorTests
{
    private const string ManifestFileName = "manifest.json";
    private const string ManifestVersion = "1.0";

    [Fact]
    public async Task EvaluateExistingEntryAsync_ShouldReuseCacheWhenManifestValid()
    {
        var fileSystem = new MockFileSystem();
        var (context, builder) = await CreateCacheEntryAsync(fileSystem);
        var evaluationTimestamp = new DateTimeOffset(2024, 8, 7, 11, 0, 0, TimeSpan.Zero);
        var evaluator = new CacheEntryEvaluator(
            fileSystem,
            new ManifestEvaluator(fileSystem),
            () => evaluationTimestamp,
            builder);

        var result = await evaluator.EvaluateExistingEntryAsync(context, ManifestFileName, ManifestVersion, CancellationToken.None);

        var reuse = Assert.IsType<CacheEvaluationResult.Reuse>(result);
        Assert.Equal(EvidenceCacheOutcome.Reused, reuse.Result.Evaluation.Outcome);
        Assert.Equal("reused", reuse.Result.Evaluation.Metadata["action"]);
        Assert.Equal(evaluationTimestamp.ToString("O"), reuse.Result.Evaluation.Metadata["evaluatedAtUtc"]);
    }

    [Fact]
    public async Task EvaluateExistingEntryAsync_ShouldDeleteCacheWhenArtifactsMismatch()
    {
        var fileSystem = new MockFileSystem();
        var (context, builder) = await CreateCacheEntryAsync(fileSystem);
        // Remove artifact to induce mismatch.
        fileSystem.File.Delete(fileSystem.Path.Combine(context.CacheDirectory, "model.json"));

        var evaluator = new CacheEntryEvaluator(
            fileSystem,
            new ManifestEvaluator(fileSystem),
            () => new DateTimeOffset(2024, 8, 7, 12, 0, 0, TimeSpan.Zero),
            builder);

        var result = await evaluator.EvaluateExistingEntryAsync(context, ManifestFileName, ManifestVersion, CancellationToken.None);

        var invalidate = Assert.IsType<CacheEvaluationResult.Invalidate>(result);
        Assert.Equal(EvidenceCacheInvalidationReason.ArtifactsMismatch, invalidate.Reason);
        Assert.False(fileSystem.Directory.Exists(context.CacheDirectory));
    }

    [Fact]
    public async Task EvaluateExistingEntryAsync_ShouldRespectRefreshRequests()
    {
        var fileSystem = new MockFileSystem();
        var (context, builder) = await CreateCacheEntryAsync(fileSystem);
        var refreshContext = context with { Refresh = true };
        var evaluator = new CacheEntryEvaluator(
            fileSystem,
            new ManifestEvaluator(fileSystem),
            () => new DateTimeOffset(2024, 8, 7, 13, 0, 0, TimeSpan.Zero),
            builder);

        var result = await evaluator.EvaluateExistingEntryAsync(refreshContext, ManifestFileName, ManifestVersion, CancellationToken.None);

        var invalidate = Assert.IsType<CacheEvaluationResult.Invalidate>(result);
        Assert.Equal(EvidenceCacheInvalidationReason.RefreshRequested, invalidate.Reason);
        Assert.False(fileSystem.Directory.Exists(context.CacheDirectory));
    }

    [Fact]
    public async Task DetermineMissingCacheReasonAsync_ShouldDetectMetadataMismatch()
    {
        var fileSystem = new MockFileSystem();
        var (context, builder) = await CreateCacheEntryAsync(fileSystem);
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["moduleFilter.includeSystemModules"] = "false",
            ["moduleFilter.moduleCount"] = "1"
        };

        var descriptorCollector = new EvidenceDescriptorCollector(fileSystem);
        var normalizer = new CacheRequestNormalizer(fileSystem, descriptorCollector);
        var request = new EvidenceCacheRequest(
            context.NormalizedRootDirectory,
            context.Command,
            context.Descriptors.First().SourcePath,
            null,
            null,
            null,
            metadata,
            Refresh: false);

        var normalized = await normalizer.TryNormalizeAsync(request, CancellationToken.None);
        var missingContext = normalized.Value;

        var evaluator = new CacheEntryEvaluator(
            fileSystem,
            new ManifestEvaluator(fileSystem),
            () => new DateTimeOffset(2024, 8, 7, 14, 0, 0, TimeSpan.Zero),
            builder);

        var result = await evaluator.DetermineMissingCacheReasonAsync(missingContext, ManifestFileName, CancellationToken.None);

        Assert.Equal(EvidenceCacheInvalidationReason.ModuleSelectionChanged, result.Reason);
        var expectedCount = missingContext.ModuleSelection.ModuleCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expectedCount, result.Metadata["expected.selection.count"]);
        Assert.NotEqual(expectedCount, result.Metadata["actual.selection.count"]);
    }

    private static async Task<(CacheRequestContext Context, CacheMetadataBuilder Builder)> CreateCacheEntryAsync(MockFileSystem fileSystem)
    {
        var root = "/cache";
        var modelPath = "/inputs/model.json";
        fileSystem.AddFile(modelPath, new MockFileData("{\"model\":true}"));

        var descriptorCollector = new EvidenceDescriptorCollector(fileSystem);
        var normalizer = new CacheRequestNormalizer(fileSystem, descriptorCollector);
        var request = new EvidenceCacheRequest(
            root,
            "ingest",
            modelPath,
            null,
            null,
            null,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            Refresh: false);

        var normalized = await normalizer.TryNormalizeAsync(request, CancellationToken.None);
        var context = normalized.Value;

        var metadataBuilder = new CacheMetadataBuilder();
        var creator = new CacheEntryCreator(
            new EvidenceCacheWriter(fileSystem),
            () => new DateTimeOffset(2024, 8, 7, 10, 0, 0, TimeSpan.Zero),
            metadataBuilder);

        var invalidationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ManifestMissing)
        };

        await creator.CreateAsync(
            context,
            ManifestFileName,
            ManifestVersion,
            EvidenceCacheInvalidationReason.ManifestMissing,
            invalidationMetadata,
            CancellationToken.None);

        return (context, metadataBuilder);
    }
}
