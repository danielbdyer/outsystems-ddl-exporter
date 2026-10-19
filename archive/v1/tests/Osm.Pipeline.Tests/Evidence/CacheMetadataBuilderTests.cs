using System;
using System.Collections.Generic;
using Osm.Pipeline;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class CacheMetadataBuilderTests
{
    [Fact]
    public void BuildOutcomeMetadata_ShouldMergeReasonAndBaseMetadata()
    {
        var builder = new CacheMetadataBuilder(new ForwardSlashPathCanonicalizer());
        var createdAt = new DateTimeOffset(2024, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var manifest = new EvidenceCacheManifest(
            "1.0",
            "abcd",
            "ingest",
            createdAt,
            createdAt,
            createdAt.AddHours(1),
            EvidenceCacheModuleSelection.Empty,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            Array.Empty<EvidenceCacheArtifact>());

        var moduleSelection = new EvidenceCacheModuleSelection(true, false, 3, "hash", new[] { "Alpha", "Beta" });
        var baseMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["reason"] = "custom",
            ["source"] = "unit-test"
        };

        var evaluatedAt = createdAt.AddMinutes(5);
        var metadata = builder.BuildOutcomeMetadata(
            evaluatedAt,
            manifest,
            moduleSelection,
            baseMetadata,
            reuse: false,
            EvidenceCacheInvalidationReason.MetadataMismatch);

        Assert.Equal("custom", metadata["reason"]);
        Assert.Equal("created", metadata["action"]);
        Assert.Equal("unit-test", metadata["source"]);
        Assert.Equal("hash", metadata["moduleSelection.hash"]);
        Assert.Equal(evaluatedAt.ToString("O"), metadata["evaluatedAtUtc"]);
        Assert.Equal(createdAt.AddHours(1).ToString("O"), metadata["manifest.expiresAtUtc"]);
    }
}
