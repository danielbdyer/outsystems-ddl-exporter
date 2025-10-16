using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
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
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

        var service = new EvidenceCacheService(
            fileSystem,
            () => new DateTimeOffset(2024, 08, 01, 12, 30, 00, TimeSpan.Zero));

        var result = await service.CacheAsync(request);

        Assert.True(result.IsSuccess);
        var cache = result.Value;
        Assert.False(string.IsNullOrWhiteSpace(cache.Manifest.Key));
        Assert.Equal("build-ssdt", cache.Manifest.Command);
        Assert.Equal(new DateTimeOffset(2024, 08, 01, 12, 30, 00, TimeSpan.Zero), cache.Manifest.CreatedAtUtc);
        Assert.Equal(cache.Manifest.CreatedAtUtc, cache.Manifest.LastValidatedAtUtc);
        Assert.Null(cache.Manifest.ExpiresAtUtc);
        Assert.Equal(EvidenceCacheOutcome.Created, cache.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.ManifestMissing, cache.Evaluation.Reason);
        Assert.Equal("manifest.missing", cache.Evaluation.Metadata["reason"]);
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
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

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
        Assert.Equal(new DateTimeOffset(2024, 08, 02, 10, 01, 00, TimeSpan.Zero), second.Value.Manifest.LastValidatedAtUtc);
        Assert.Equal(
            first.Value.Manifest.Metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            second.Value.Manifest.Metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(
            first.Value.Manifest.Artifacts.OrderBy(static artifact => artifact.Type),
            second.Value.Manifest.Artifacts.OrderBy(static artifact => artifact.Type));
        Assert.Equal(initialManifestTimestamp, fileSystem.File.GetLastWriteTimeUtc(manifestPath));
        Assert.Equal(initialModelTimestamp, fileSystem.File.GetLastWriteTimeUtc(modelArtifactPath));
        Assert.Equal(2, callCount);

        Assert.Equal(EvidenceCacheOutcome.Created, first.Value.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheOutcome.Reused, second.Value.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.None, second.Value.Evaluation.Reason);
        Assert.Equal("cache.reused", second.Value.Evaluation.Metadata["reason"]);
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
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

        var service = new EvidenceCacheService(
            fileSystem,
            () => new DateTimeOffset(2024, 08, 01, 13, 00, 00, TimeSpan.Zero));

        var first = await service.CacheAsync(baseRequest);
        var second = await service.CacheAsync(baseRequest);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Manifest.Key, second.Value.Manifest.Key);
        Assert.Equal(first.Value.CacheDirectory, second.Value.CacheDirectory);
        Assert.Equal(EvidenceCacheOutcome.Created, first.Value.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheOutcome.Reused, second.Value.Evaluation.Outcome);

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
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

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
        Assert.Equal(EvidenceCacheOutcome.Created, initial.Value.Evaluation.Outcome);

        fileSystem.AddFile(fileSystem.Path.Combine(cacheDir, "stale.tmp"), new MockFileData("old"));

        var refreshed = await service.CacheAsync(request with { Refresh = true });
        Assert.True(refreshed.IsSuccess);
        Assert.False(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDir, "stale.tmp")));
        Assert.Equal(new DateTimeOffset(2024, 08, 02, 12, 00, 00, TimeSpan.Zero), refreshed.Value.Manifest.CreatedAtUtc);
        Assert.Equal(EvidenceCacheOutcome.Created, refreshed.Value.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.RefreshRequested, refreshed.Value.Evaluation.Reason);
        Assert.Equal("refresh.requested", refreshed.Value.Evaluation.Metadata["reason"]);

        var refreshedModelTimestamp = fileSystem.File.GetLastWriteTimeUtc(modelPath);
        Assert.NotEqual(initialModelTimestamp, refreshedModelTimestamp);
        Assert.Empty(timestamps);
    }

    [Fact]
    public async Task CacheAsync_ShouldExpireCache_WhenTtlElapsed()
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
            Metadata: new Dictionary<string, string?>
            {
                ["cache.ttlSeconds"] = "3600",
            },
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 03, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 03, 10, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 03, 11, 00, 00, TimeSpan.Zero),
        });

        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var initial = await service.CacheAsync(request);
        Assert.True(initial.IsSuccess);
        Assert.Equal(new DateTimeOffset(2024, 08, 03, 09, 00, 00, TimeSpan.Zero), initial.Value.Manifest.ExpiresAtUtc);
        Assert.Equal(EvidenceCacheInvalidationReason.ManifestMissing, initial.Value.Evaluation.Reason);

        var expired = await service.CacheAsync(request);
        Assert.True(expired.IsSuccess);
        Assert.Equal(EvidenceCacheOutcome.Created, expired.Value.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.ManifestExpired, expired.Value.Evaluation.Reason);
        Assert.Equal("manifest.expired", expired.Value.Evaluation.Metadata["reason"]);
        Assert.Equal(new DateTimeOffset(2024, 08, 03, 12, 00, 00, TimeSpan.Zero), expired.Value.Manifest.ExpiresAtUtc);
        Assert.Empty(timestamps);
    }

    [Fact]
    public async Task CacheAsync_ShouldPruneExpiredEntries_WhenRetentionMaxAgeConfigured()
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
            Metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["cache.ttlSeconds"] = "3600",
            },
            Refresh: false,
            RetentionMaxAge: TimeSpan.FromHours(1),
            RetentionMaxEntries: null);

        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 06, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 06, 10, 05, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 06, 10, 10, 00, TimeSpan.Zero),
        });

        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var initial = await service.CacheAsync(request);
        Assert.True(initial.IsSuccess);

        var manifestPath = fileSystem.Path.Combine(initial.Value.CacheDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<EvidenceCacheManifest>(fileSystem.File.ReadAllText(manifestPath))!;
        var staleManifest = manifest with
        {
            CreatedAtUtc = new DateTimeOffset(2024, 08, 06, 08, 00, 00, TimeSpan.Zero),
            LastValidatedAtUtc = new DateTimeOffset(2024, 08, 06, 08, 00, 00, TimeSpan.Zero)
        };

        fileSystem.File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(staleManifest, new JsonSerializerOptions { WriteIndented = true }));

        var rebuilt = await service.CacheAsync(request);

        Assert.True(rebuilt.IsSuccess);
        Assert.Equal(EvidenceCacheOutcome.Created, rebuilt.Value.Evaluation.Outcome);
        Assert.Equal(new DateTimeOffset(2024, 08, 06, 10, 10, 00, TimeSpan.Zero), rebuilt.Value.Manifest.CreatedAtUtc);
        Assert.Equal("1", rebuilt.Value.Evaluation.Metadata["pruned.total"]);
        Assert.Equal("1", rebuilt.Value.Evaluation.Metadata["pruned.expired"]);
        Assert.Equal("0", rebuilt.Value.Evaluation.Metadata["pruned.remaining"]);
        Assert.Contains("expired:", rebuilt.Value.Evaluation.Metadata["pruned.entries"]);

        var directories = fileSystem.Directory.EnumerateDirectories("/cache").ToArray();
        Assert.Single(directories);
        Assert.Equal(initial.Value.CacheDirectory, directories[0]);
        Assert.Empty(timestamps);
    }

    [Fact]
    public async Task CacheAsync_ShouldInvalidateCache_WhenModuleSelectionChanges()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
        });

        var baseMetadata = new Dictionary<string, string?>
        {
            ["moduleFilter.modules"] = "ModuleA,ModuleB",
            ["moduleFilter.moduleCount"] = "2",
        };

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: baseMetadata,
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 04, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 04, 09, 30, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 04, 09, 45, 00, TimeSpan.Zero),
        });

        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var initial = await service.CacheAsync(request);
        Assert.True(initial.IsSuccess);
        Assert.Equal(EvidenceCacheInvalidationReason.ManifestMissing, initial.Value.Evaluation.Reason);

        var manifestPath = fileSystem.Path.Combine(initial.Value.CacheDirectory, "manifest.json");
        var manifestJson = fileSystem.File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<EvidenceCacheManifest>(manifestJson)!;

        var tamperedSelection = new EvidenceCacheModuleSelection(
            manifest.ModuleSelection?.IncludeSystemModules ?? true,
            manifest.ModuleSelection?.IncludeInactiveModules ?? true,
            1,
            manifest.ModuleSelection?.ModulesHash,
            new[] { "ModuleA" });

        var tamperedManifest = manifest with { ModuleSelection = tamperedSelection };
        fileSystem.File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(tamperedManifest, new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(fileSystem.File.Exists(manifestPath));
        var reloaded = JsonSerializer.Deserialize<EvidenceCacheManifest>(fileSystem.File.ReadAllText(manifestPath));
        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded!.ModuleSelection?.ModuleCount);
        await using (var stream = fileSystem.File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var asyncReloaded = await JsonSerializer.DeserializeAsync<EvidenceCacheManifest>(stream);
            Assert.NotNull(asyncReloaded);
            Assert.Equal(1, asyncReloaded!.ModuleSelection?.ModuleCount);
        }

        var narrowed = await service.CacheAsync(request);

        Assert.True(narrowed.IsSuccess);
        Assert.Equal(initial.Value.CacheDirectory, narrowed.Value.CacheDirectory);
        Assert.Equal(initial.Value.Manifest.Key, narrowed.Value.Manifest.Key);
        Assert.Equal(EvidenceCacheOutcome.Created, narrowed.Value.Evaluation.Outcome);
        var actualReason = narrowed.Value.Evaluation.Reason;
        var metadataReason = narrowed.Value.Evaluation.Metadata.TryGetValue("reason", out var reasonValue)
            ? reasonValue
            : null;
        Assert.Equal(EvidenceCacheInvalidationReason.ModuleSelectionChanged, actualReason);
        Assert.Equal("module.selection.changed", metadataReason);
        Assert.Empty(timestamps);
    }

    [Fact]
    public async Task CacheAsync_ShouldInvalidateCache_WhenMetadataDiffers()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
        });

        var baseMetadata = new Dictionary<string, string?>
        {
            ["policy.mode"] = "EvidenceGated",
        };

        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: baseMetadata,
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 05, 07, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 05, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 05, 08, 15, 00, TimeSpan.Zero),
        });

        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var initial = await service.CacheAsync(request);
        Assert.True(initial.IsSuccess);
        Assert.Equal(EvidenceCacheInvalidationReason.ManifestMissing, initial.Value.Evaluation.Reason);

        var aggressiveRequest = request with
        {
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["policy.mode"] = "Aggressive",
            }
        };

        var rebuilt = await service.CacheAsync(aggressiveRequest);
        Assert.True(rebuilt.IsSuccess);
        Assert.Equal(EvidenceCacheOutcome.Created, rebuilt.Value.Evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.MetadataMismatch, rebuilt.Value.Evaluation.Reason);
        Assert.Equal("metadata.mismatch", rebuilt.Value.Evaluation.Metadata["reason"]);
        Assert.Single(timestamps, new DateTimeOffset(2024, 08, 05, 08, 15, 00, TimeSpan.Zero));
    }

    [Fact]
    public async Task CacheAsync_ShouldTrimOldestEntries_WhenCapacityExceeded()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
        });

        var baseRequest = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build-ssdt",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["policy.mode"] = "EvidenceGated",
            },
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: 1);

        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 07, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 07, 08, 05, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 07, 08, 10, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 07, 08, 10, 30, TimeSpan.Zero),
        });

        var service = new EvidenceCacheService(fileSystem, () => timestamps.Dequeue());

        var first = await service.CacheAsync(baseRequest);
        Assert.True(first.IsSuccess);

        var alternateMetadata = new Dictionary<string, string?>(baseRequest.Metadata, StringComparer.Ordinal)
        {
            ["policy.mode"] = "Aggressive",
        };

        var second = await service.CacheAsync(baseRequest with { Metadata = alternateMetadata });
        Assert.True(second.IsSuccess);

        var reused = await service.CacheAsync(baseRequest);

        Assert.True(reused.IsSuccess);
        Assert.Equal(EvidenceCacheOutcome.Reused, reused.Value.Evaluation.Outcome);
        Assert.Equal("1", reused.Value.Evaluation.Metadata["pruned.total"]);
        Assert.Equal("1", reused.Value.Evaluation.Metadata["pruned.capacity"]);
        Assert.Equal("1", reused.Value.Evaluation.Metadata["pruned.remaining"]);
        Assert.Contains(second.Value.Manifest.Key, reused.Value.Evaluation.Metadata["pruned.entries"]);
        Assert.False(fileSystem.Directory.Exists(second.Value.CacheDirectory));
        Assert.Single(fileSystem.Directory.EnumerateDirectories("/cache"));
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
            Refresh: false,
            RetentionMaxAge: null,
            RetentionMaxEntries: null);

        var service = new EvidenceCacheService(fileSystem, () => DateTimeOffset.UtcNow);
        var result = await service.CacheAsync(request);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("cache.model.notFound", error.Code);
    }
}
