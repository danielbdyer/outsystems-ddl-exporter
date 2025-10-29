using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class CacheEntryCreatorTests
{
    [Fact]
    public async Task CreateAsync_ShouldPersistManifestAndEvaluation()
    {
        var fileSystem = new MockFileSystem();
        var cacheDirectory = "/cache/abcd";
        var modelPath = "/inputs/model.json";
        var modelContent = "{\"model\":true}";
        fileSystem.AddFile(modelPath, new MockFileData(modelContent));

        var descriptor = CreateDescriptor(EvidenceArtifactType.Model, modelPath, modelContent, ".json");
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["cache.ttlSeconds"] = "3600"
        };

        var moduleSelection = new EvidenceCacheModuleSelection(true, true, 1, "hash", new[] { "Alpha" });
        var context = new CacheRequestContext(
            "/cache",
            cacheDirectory,
            "ingest",
            "abcd",
            metadata,
            new[] { descriptor },
            moduleSelection,
            Refresh: false);

        var invalidationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["reason"] = EvidenceCacheReasonMapper.Map(EvidenceCacheInvalidationReason.ManifestMissing)
        };

        var creationTimestamp = new DateTimeOffset(2024, 8, 7, 9, 0, 0, TimeSpan.Zero);
        var writer = new EvidenceCacheWriter(fileSystem);
        var metadataBuilder = new CacheMetadataBuilder();
        var creator = new CacheEntryCreator(writer, () => creationTimestamp, metadataBuilder);

        var result = await creator.CreateAsync(
            context,
            "manifest.json",
            "1.0",
            EvidenceCacheInvalidationReason.ManifestMissing,
            invalidationMetadata,
            CancellationToken.None);

        Assert.Equal(cacheDirectory, result.CacheDirectory);
        Assert.Equal(creationTimestamp, result.Manifest.CreatedAtUtc);
        Assert.Equal(EvidenceCacheOutcome.Created, result.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.ManifestMissing, result.Evaluation.Reason);
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDirectory, "manifest.json")));
        Assert.Equal("created", result.Evaluation.Metadata["action"]);
    }

    private static EvidenceArtifactDescriptor CreateDescriptor(
        EvidenceArtifactType type,
        string sourcePath,
        string content,
        string extension)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new EvidenceArtifactDescriptor(type, sourcePath, hash, bytes.Length, extension);
    }
}
