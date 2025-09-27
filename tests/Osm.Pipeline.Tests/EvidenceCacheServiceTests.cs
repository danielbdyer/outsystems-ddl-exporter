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
    public async Task CacheAsync_ShouldPersistArtifactsAndManifest()
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
    public async Task CacheAsync_ShouldDeleteExistingDirectory_WhenRefreshRequested()
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

        var service = new EvidenceCacheService(
            fileSystem,
            () => new DateTimeOffset(2024, 08, 01, 14, 00, 00, TimeSpan.Zero));

        var initial = await service.CacheAsync(request);
        Assert.True(initial.IsSuccess);

        var cacheDir = initial.Value.CacheDirectory;
        fileSystem.AddFile(fileSystem.Path.Combine(cacheDir, "stale.tmp"), new MockFileData("old"));

        var refreshed = await service.CacheAsync(request with { Refresh = true });
        Assert.True(refreshed.IsSuccess);
        Assert.False(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDir, "stale.tmp")));
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
