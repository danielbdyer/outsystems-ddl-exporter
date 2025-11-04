using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class EvidenceCacheServiceTests
{
    [Fact]
    public async Task CacheAsync_ShouldReuseExistingCache()
    {
        var fileSystem = new MockFileSystem();
        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 8, 7, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 8, 7, 10, 0, 0, TimeSpan.Zero)
        });
        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var request = CreateRequest(fileSystem, refresh: false);
        var first = await service.CacheAsync(request, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(EvidenceCacheOutcome.Created, first.Value.Evaluation.Outcome);

        var second = await service.CacheAsync(request, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(EvidenceCacheOutcome.Reused, second.Value.Evaluation.Outcome);
        Assert.Equal("reused", second.Value.Evaluation.Metadata["action"]);
    }

    [Fact]
    public async Task CacheAsync_ShouldForceRefresh()
    {
        var fileSystem = new MockFileSystem();
        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 8, 7, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 8, 7, 10, 0, 0, TimeSpan.Zero)
        });
        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var request = CreateRequest(fileSystem, refresh: false);
        await service.CacheAsync(request, CancellationToken.None);

        var refreshRequest = CreateRequest(fileSystem, refresh: true);
        var refreshed = await service.CacheAsync(refreshRequest, CancellationToken.None);

        Assert.True(refreshed.IsSuccess);
        Assert.Equal(EvidenceCacheInvalidationReason.RefreshRequested, refreshed.Value.Evaluation.Reason);
        Assert.Equal("created", refreshed.Value.Evaluation.Metadata["action"]);
    }

    [Fact]
    public async Task CacheAsync_ShouldDetectMetadataMismatchWhenDirectoryMissing()
    {
        var fileSystem = new MockFileSystem();
        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 8, 7, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 8, 7, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 8, 7, 11, 0, 0, TimeSpan.Zero)
        });
        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var initial = CreateRequest(fileSystem, refresh: false);
        await service.CacheAsync(initial, CancellationToken.None);

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["moduleFilter.includeSystemModules"] = "false"
        };

        var request = CreateRequest(fileSystem, metadata, refresh: false);
        var result = await service.CacheAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EvidenceCacheInvalidationReason.ModuleSelectionChanged, result.Value.Evaluation.Reason);
        Assert.Equal("created", result.Value.Evaluation.Metadata["action"]);
    }

    private static EvidenceCacheRequest CreateRequest(
        MockFileSystem fileSystem,
        IReadOnlyDictionary<string, string?>? metadata = null,
        bool refresh = false)
    {
        var root = "/cache";
        var modelPath = "/inputs/model.json";
        if (!fileSystem.FileExists(modelPath))
        {
            fileSystem.AddFile(modelPath, new MockFileData("{\"model\":true}"));
        }

        return new EvidenceCacheRequest(
            root,
            "ingest",
            modelPath,
            null,
            null,
            null,
            metadata ?? new Dictionary<string, string?>(StringComparer.Ordinal),
            refresh);
    }
}
