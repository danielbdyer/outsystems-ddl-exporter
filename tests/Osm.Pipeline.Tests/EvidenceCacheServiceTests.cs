using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests;

public sealed class EvidenceCacheServiceTests
{
    [Fact]
    public async Task CacheAsync_ShouldCacheArtifactsAndManifest_OnFirstRun()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
            ["/inputs/profile.json"] = new MockFileData("{\"profile\":true}"),
            ["/inputs/config.json"] = new MockFileData("{\"config\":true}"),
        });

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: "/inputs/profile.json",
            DmmPath: null,
            ConfigPath: "/inputs/config.json",
            Metadata: new Dictionary<string, string?>
            {
                ["policy.mode"] = "EvidenceGated",
                ["emission.concatenated"] = "false",
            },
            Refresh: false);

        var service = new EvidenceCacheService(
            fileSystem,
            () => new DateTimeOffset(2024, 08, 01, 12, 30, 00, TimeSpan.Zero));

        var result = await service.CacheAsync(request);

        Assert.True(result.IsSuccess);
        var cache = result.Value;
        Assert.False(string.IsNullOrWhiteSpace(cache.Manifest.Key));
        Assert.Equal("build-ssdt", cache.Manifest.Command);
        Assert.Equal(new DateTimeOffset(2024, 08, 01, 12, 30, 00, TimeSpan.Zero), cache.Manifest.CreatedAtUtc);
        Assert.Equal("EvidenceGated", cache.Manifest.Metadata["policy.mode"]);

        var modelArtifact = Assert.Single(cache.Manifest.Artifacts.Where(static a => a.Type == EvidenceArtifactType.Model));
        Assert.Equal("model.json", modelArtifact.RelativePath);
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cache.CacheDirectory, modelArtifact.RelativePath)));

        var profileArtifact = Assert.Single(cache.Manifest.Artifacts.Where(static a => a.Type == EvidenceArtifactType.Profile));
        Assert.Equal("profile.json", profileArtifact.RelativePath);
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cache.CacheDirectory, profileArtifact.RelativePath)));

        var configArtifact = Assert.Single(cache.Manifest.Artifacts.Where(static a => a.Type == EvidenceArtifactType.Configuration));
        Assert.Equal("config.json", configArtifact.RelativePath);
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cache.CacheDirectory, configArtifact.RelativePath)));
    }

    [Fact]
    public async Task CacheAsync_ShouldReuseExistingCache_WhenManifestMatches()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
            ["/inputs/profile.json"] = new MockFileData("{\"profile\":true}"),
        });

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: "/inputs/profile.json",
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>
            {
                ["policy.mode"] = "EvidenceGated",
            },
            Refresh: false);

        var callCount = 0;
        DateTimeOffset TimestampProvider()
        {
            callCount++;
            var baseTimestamp = new DateTimeOffset(2024, 08, 02, 10, 00, 00, TimeSpan.Zero);
            return baseTimestamp.AddMinutes(callCount - 1);
        }

        var service = new EvidenceCacheService(fileSystem, TimestampProvider);

        var first = await service.CacheAsync(request);
        Assert.True(first.IsSuccess);

        var cacheDirectory = first.Value.CacheDirectory;
        var manifestPath = fileSystem.Path.Combine(cacheDirectory, "manifest.json");
        var modelArtifactPath = fileSystem.Path.Combine(cacheDirectory, "model.json");

        var initialManifestTimestamp = fileSystem.File.GetLastWriteTimeUtc(manifestPath);
        var initialModelTimestamp = fileSystem.File.GetLastWriteTimeUtc(modelArtifactPath);

        var second = await service.CacheAsync(request);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.CacheDirectory, second.Value.CacheDirectory);
        Assert.Equal(first.Value.Manifest.Key, second.Value.Manifest.Key);
        Assert.Equal(first.Value.Manifest.Command, second.Value.Manifest.Command);
        Assert.Equal(first.Value.Manifest.CreatedAtUtc, second.Value.Manifest.CreatedAtUtc);
        Assert.Equal(
            first.Value.Manifest.Metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            second.Value.Manifest.Metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(
            first.Value.Manifest.Artifacts.OrderBy(static artifact => artifact.Type),
            second.Value.Manifest.Artifacts.OrderBy(static artifact => artifact.Type));
        Assert.Equal(initialManifestTimestamp, fileSystem.File.GetLastWriteTimeUtc(manifestPath));
        Assert.Equal(initialModelTimestamp, fileSystem.File.GetLastWriteTimeUtc(modelArtifactPath));
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task CacheAsync_ShouldProduceDeterministicKeys()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
            ["/inputs/profile.json"] = new MockFileData("{\"profile\":true}"),
        });

        var baseRequest = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: "/inputs/profile.json",
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>
            {
                ["policy.mode"] = "EvidenceGated",
                ["emission.concatenated"] = "false",
            },
            Refresh: false);

        var service = new EvidenceCacheService(
            fileSystem,
            () => new DateTimeOffset(2024, 08, 01, 13, 00, 00, TimeSpan.Zero));

        var first = await service.CacheAsync(baseRequest);
        var second = await service.CacheAsync(baseRequest);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Manifest.Key, second.Value.Manifest.Key);
        Assert.Equal(first.Value.CacheDirectory, second.Value.CacheDirectory);

        var aggressiveRequest = baseRequest with
        {
            Metadata = new Dictionary<string, string?>
            {
                ["policy.mode"] = "Aggressive",
            }
        };

        var aggressive = await service.CacheAsync(aggressiveRequest);
        Assert.True(aggressive.IsSuccess);
        Assert.NotEqual(first.Value.Manifest.Key, aggressive.Value.Manifest.Key);
    }

    [Fact]
    public async Task CacheAsync_ShouldRebuildCache_WhenRefreshRequested()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
        });

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>(),
            Refresh: false);

        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 02, 11, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 02, 12, 00, 00, TimeSpan.Zero),
        });

        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var initial = await service.CacheAsync(request);
        Assert.True(initial.IsSuccess);

        var cacheDir = initial.Value.CacheDirectory;
        var modelPath = fileSystem.Path.Combine(cacheDir, "model.json");
        var initialModelTimestamp = fileSystem.File.GetLastWriteTimeUtc(modelPath);

        fileSystem.AddFile(fileSystem.Path.Combine(cacheDir, "stale.tmp"), new MockFileData("old"));

        var refreshed = await service.CacheAsync(request with { Refresh = true });
        Assert.True(refreshed.IsSuccess);
        Assert.False(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDir, "stale.tmp")));
        Assert.Equal(new DateTimeOffset(2024, 08, 02, 12, 00, 00, TimeSpan.Zero), refreshed.Value.Manifest.CreatedAtUtc);

        var refreshedModelTimestamp = fileSystem.File.GetLastWriteTimeUtc(modelPath);
        Assert.NotEqual(initialModelTimestamp, refreshedModelTimestamp);
        Assert.Empty(timestamps);
    }

    [Fact]
    public async Task CacheAsync_ShouldFail_WhenModelMissing()
    {
        var fileSystem = new MockFileSystem();
        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>(),
            Refresh: false);

        var service = new EvidenceCacheService(fileSystem, () => DateTimeOffset.UtcNow);
        var result = await service.CacheAsync(request);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("cache.model.notFound", error.Code);
    }
}
