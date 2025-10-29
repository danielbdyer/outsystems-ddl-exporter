using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class CacheRequestNormalizerTests
{
    [Fact]
    public async Task TryNormalizeAsync_ShouldReturnErrorWhenRootMissing()
    {
        var fileSystem = new MockFileSystem();
        var descriptorCollector = new EvidenceDescriptorCollector(fileSystem);
        var normalizer = new CacheRequestNormalizer(fileSystem, descriptorCollector);
        var request = new EvidenceCacheRequest(string.Empty, "ingest", null, null, null, null, new Dictionary<string, string?>(), false);

        var result = await normalizer.TryNormalizeAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(Assert.Single(result.Errors));
        Assert.Equal("cache.root.missing", error.Code);
    }

    [Fact]
    public async Task TryNormalizeAsync_ShouldComputeDeterministicKey()
    {
        var fileSystem = new MockFileSystem();
        var root = "/cache";
        var modelPath = "/inputs/model.json";
        var profilePath = "/inputs/profile.json";

        fileSystem.AddFile(modelPath, new MockFileData("{\"model\":true}"));
        fileSystem.AddFile(profilePath, new MockFileData("{\"profile\":true}"));

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["moduleFilter.modules"] = "Alpha, Beta",
            ["moduleFilter.includeSystemModules"] = "false"
        };

        var descriptorCollector = new EvidenceDescriptorCollector(fileSystem);
        var normalizer = new CacheRequestNormalizer(fileSystem, descriptorCollector);
        var request = new EvidenceCacheRequest(
            root,
            "ingest",
            modelPath,
            profilePath,
            null,
            null,
            metadata,
            refresh: false);

        var result = await normalizer.TryNormalizeAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var context = result.Value;
        Assert.Equal(fileSystem.Path.Combine(root, context.Key), context.CacheDirectory);
        Assert.Equal("ingest", context.Command);
        Assert.Equal(2, context.ModuleSelection.Modules.Count);

        // Expected key computed by the same hashing contract as the legacy implementation.
        Assert.Equal("fb430f6e2a9d394c", context.Key);
    }
}
