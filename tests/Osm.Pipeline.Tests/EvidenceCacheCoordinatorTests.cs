using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class EvidenceCacheCoordinatorTests
{
    [Fact]
    public async Task CacheAsync_persists_results_and_records_creation_metadata()
    {
        using var cacheDirectory = new TempDirectory();
        var manifest = CreateManifest();
        var evaluation = new EvidenceCacheEvaluation(
            EvidenceCacheOutcome.Created,
            EvidenceCacheInvalidationReason.ManifestMissing,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["cacheGeneration"] = "initial"
            });
        var cacheResult = new EvidenceCacheResult(cacheDirectory.Path, manifest, evaluation);
        var cacheService = new FakeEvidenceCacheService(Result<EvidenceCacheResult>.Success(cacheResult));
        var coordinator = new EvidenceCacheCoordinator(cacheService);
        var log = new PipelineExecutionLogBuilder(TimeProvider.System);
        var options = CreateOptions(cacheDirectory.Path);

        var result = await coordinator.CacheAsync(options, log);

        Assert.True(result.IsSuccess);
        Assert.Equal(cacheResult, result.Value);
        Assert.NotNull(cacheService.LastRequest);
        Assert.Equal(cacheDirectory.Path.Trim(), cacheService.LastRequest!.RootDirectory);

        var entries = log.Build().Entries;
        Assert.Contains(entries, entry => entry.Step == "evidence.cache.requested");
        var persisted = Assert.Single(entries, entry => entry.Step == "evidence.cache.persisted");
        Assert.Equal("Persisted evidence cache manifest.", persisted.Message);
        Assert.Equal("initial", persisted.Metadata["cacheGeneration"]);
    }

    [Fact]
    public async Task CacheAsync_records_reuse_outcome_and_metadata()
    {
        using var cacheDirectory = new TempDirectory();
        var manifest = CreateManifest();
        var evaluation = new EvidenceCacheEvaluation(
            EvidenceCacheOutcome.Reused,
            EvidenceCacheInvalidationReason.None,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["lastValidated"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
        var cacheResult = new EvidenceCacheResult(cacheDirectory.Path, manifest, evaluation);
        var cacheService = new FakeEvidenceCacheService(Result<EvidenceCacheResult>.Success(cacheResult));
        var coordinator = new EvidenceCacheCoordinator(cacheService);
        var log = new PipelineExecutionLogBuilder(TimeProvider.System);
        var options = CreateOptions(cacheDirectory.Path);

        var result = await coordinator.CacheAsync(options, log);

        Assert.True(result.IsSuccess);
        Assert.Equal(cacheResult, result.Value);

        var entries = log.Build().Entries;
        Assert.Contains(entries, entry => entry.Step == "evidence.cache.requested");
        var reused = Assert.Single(entries, entry => entry.Step == "evidence.cache.reused");
        Assert.Equal("Reused evidence cache manifest.", reused.Message);
        Assert.True(reused.Metadata.ContainsKey("lastValidated"));
    }

    private static EvidenceCacheManifest CreateManifest()
    {
        return new EvidenceCacheManifest(
            Version: "1.0",
            Key: "cache-key",
            Command: "build-ssdt",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastValidatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            ModuleSelection: EvidenceCacheModuleSelection.Empty,
            Metadata: new Dictionary<string, string?>(StringComparer.Ordinal),
            Artifacts: new List<EvidenceCacheArtifact>());
    }

    private static EvidenceCachePipelineOptions CreateOptions(string rootDirectory)
    {
        return new EvidenceCachePipelineOptions(
            rootDirectory + "  ",
            Refresh: false,
            Command: "build-ssdt",
            ModelPath: "model.json",
            ProfilePath: "profile.json",
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>());
    }

    private sealed class FakeEvidenceCacheService : IEvidenceCacheService
    {
        private readonly Result<EvidenceCacheResult> _result;

        public FakeEvidenceCacheService(Result<EvidenceCacheResult> result)
        {
            _result = result;
        }

        public EvidenceCacheRequest? LastRequest { get; private set; }

        public Task<Result<EvidenceCacheResult>> CacheAsync(
            EvidenceCacheRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
