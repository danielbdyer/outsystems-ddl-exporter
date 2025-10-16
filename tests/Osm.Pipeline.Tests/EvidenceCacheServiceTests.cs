using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Null(cache.Manifest.ExpiresAtUtc);
        Assert.NotNull(cache.Manifest.ModuleSelection);
        Assert.Empty(cache.Manifest.ModuleSelection!.Modules);
        Assert.Equal(EvidenceCacheDecisionKind.Created, cache.Execution.Decision);
        Assert.Equal(EvidenceCacheDecisionKind.Created.ToString(), cache.Execution.Metadata["status"]);

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
        Assert.Equal(EvidenceCacheDecisionKind.Created, first.Value.Execution.Decision);
        Assert.Equal(EvidenceCacheDecisionKind.Reused, second.Value.Execution.Decision);
        Assert.Equal(first.Value.Manifest.CreatedAtUtc, second.Value.Manifest.CreatedAtUtc);

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
        Assert.Equal(EvidenceCacheDecisionKind.Created, aggressive.Value.Execution.Decision);
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
        Assert.Equal(EvidenceCacheDecisionKind.Refreshed, refreshed.Value.Execution.Decision);
        Assert.Equal(bool.TrueString, refreshed.Value.Execution.Metadata["reason.refreshRequested"]);
        Assert.False(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDir, "stale.tmp")));
    }

    [Fact]
    public async Task CacheAsync_ShouldRespectTimeToLiveExpiry()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
        });

        var currentTime = new DateTimeOffset(2024, 08, 01, 15, 00, 00, TimeSpan.Zero);
        var service = new EvidenceCacheService(fileSystem, () => currentTime);

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>
            {
                ["policy.mode"] = "EvidenceGated",
            },
            Refresh: false,
            TimeToLive: TimeSpan.FromMinutes(30));

        var first = await service.CacheAsync(request);
        Assert.True(first.IsSuccess);
        Assert.Equal(EvidenceCacheDecisionKind.Created, first.Value.Execution.Decision);
        Assert.Equal(currentTime, first.Value.Manifest.CreatedAtUtc);
        Assert.Equal(currentTime.AddMinutes(30), first.Value.Manifest.ExpiresAtUtc);

        currentTime = currentTime.AddMinutes(10);
        var second = await service.CacheAsync(request);
        Assert.True(second.IsSuccess);
        Assert.Equal(EvidenceCacheDecisionKind.Reused, second.Value.Execution.Decision);
        Assert.Equal(first.Value.Manifest.CreatedAtUtc, second.Value.Manifest.CreatedAtUtc);

        currentTime = currentTime.AddMinutes(25);
        var third = await service.CacheAsync(request);
        Assert.True(third.IsSuccess);
        Assert.Equal(EvidenceCacheDecisionKind.Refreshed, third.Value.Execution.Decision);
        Assert.Equal(bool.TrueString, third.Value.Execution.Metadata["reason.ttlExpired"]);
        Assert.True(third.Value.Manifest.CreatedAtUtc > first.Value.Manifest.CreatedAtUtc);
    }

    [Fact]
    public async Task CacheAsync_ShouldInvalidate_WhenModuleSelectionDrifts()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
        });

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["policy.mode"] = "EvidenceGated",
        };

        const string moduleNormalized = "Sales";
        metadata["moduleFilter.modules"] = moduleNormalized;
        metadata["moduleFilter.modules.normalized"] = moduleNormalized;
        metadata["moduleFilter.modules.count"] = "1";
        metadata["moduleFilter.modules.hash"] = ComputeHash(moduleNormalized);

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: metadata,
            Refresh: false);

        var service = new EvidenceCacheService(
            fileSystem,
            () => new DateTimeOffset(2024, 08, 01, 16, 00, 00, TimeSpan.Zero));

        var first = await service.CacheAsync(request);
        Assert.True(first.IsSuccess);

        var manifestPath = fileSystem.Path.Combine(first.Value.CacheDirectory, "manifest.json");
        var manifestJson = JsonNode.Parse(fileSystem.File.ReadAllText(manifestPath))!;
        var moduleSelection = manifestJson["ModuleSelection"] as JsonObject ?? new JsonObject();
        moduleSelection["Hash"] = ComputeHash("Sales,Finance");
        moduleSelection["Modules"] = new JsonArray("Sales", "Finance");
        manifestJson["ModuleSelection"] = moduleSelection;
        fileSystem.File.WriteAllText(manifestPath, manifestJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var second = await service.CacheAsync(request);
        Assert.True(second.IsSuccess);
        Assert.Equal(EvidenceCacheDecisionKind.Refreshed, second.Value.Execution.Decision);
        Assert.Equal(bool.TrueString, second.Value.Execution.Metadata["reason.moduleSelectionChanged"]);
        Assert.Equal("1", second.Value.Execution.Metadata["requested.moduleSelection.count"]);
    }

    [Fact]
    public async Task CacheAsync_ShouldInvalidate_WhenMetadataDiffers()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
        });

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["policy.mode"] = "EvidenceGated",
            ["emission.concatenated"] = "false",
        };

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: metadata,
            Refresh: false);

        var service = new EvidenceCacheService(
            fileSystem,
            () => new DateTimeOffset(2024, 08, 01, 17, 00, 00, TimeSpan.Zero));

        var first = await service.CacheAsync(request);
        Assert.True(first.IsSuccess);

        var manifestPath = fileSystem.Path.Combine(first.Value.CacheDirectory, "manifest.json");
        var manifestJson = JsonNode.Parse(fileSystem.File.ReadAllText(manifestPath))!;
        var manifestMetadata = manifestJson["Metadata"]!.AsObject();
        manifestMetadata["policy.mode"] = "Aggressive";
        manifestJson["Metadata"] = manifestMetadata;
        fileSystem.File.WriteAllText(manifestPath, manifestJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var second = await service.CacheAsync(request);
        Assert.True(second.IsSuccess);
        Assert.Equal(EvidenceCacheDecisionKind.Refreshed, second.Value.Execution.Decision);
        Assert.Equal(bool.TrueString, second.Value.Execution.Metadata["reason.metadataMismatch"]);
        Assert.Equal("EvidenceGated", second.Value.Manifest.Metadata["policy.mode"]);
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

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
