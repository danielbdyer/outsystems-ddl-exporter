using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Xunit;

namespace Osm.Pipeline.Tests.Evidence;

public class EvidenceCacheCoordinatorTests
{
    [Fact]
    public async Task CoordinateAsync_WhenCacheCreated_RecordsPersistedTelemetry()
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["baseline"] = "edge-case"
        };
        var evaluationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["reasonCode"] = "new"
        };
        var manifest = CreateManifest();
        var evaluation = CreateEvaluation(
            EvidenceCacheOutcome.Created,
            EvidenceCacheInvalidationReason.None,
            evaluationMetadata);
        var cacheResult = new EvidenceCacheResult("/cache", manifest, evaluation);
        var service = new StubEvidenceCacheService(Result<EvidenceCacheResult>.Success(cacheResult));
        var coordinator = new EvidenceCacheCoordinator(service);
        var log = new PipelineExecutionLogBuilder(TimeProvider.System);
        var options = new EvidenceCachePipelineOptions(
            "  /cache-root  ",
            Refresh: false,
            Command: "build-ssdt",
            ModelPath: "model.json",
            ProfilePath: "profile.json",
            DmmPath: null,
            ConfigPath: null,
            Metadata: metadata);

        var coordination = await coordinator.CoordinateAsync(options, log);

        Assert.True(coordination.IsSuccess);
        Assert.NotNull(coordination.Value);
        Assert.Equal("/cache-root", service.LastRequest!.RootDirectory);
        Assert.False(service.LastRequest.Refresh);
        Assert.Same(metadata, service.LastRequest.Metadata);

        var entries = log.Build().Entries;
        var requested = Assert.Single(entries, entry => entry.Step == "evidence.cache.requested");
        Assert.Equal("Caching pipeline inputs.", requested.Message);
        Assert.Equal("/cache-root", requested.Metadata["rootDirectory"]);
        Assert.Equal("false", requested.Metadata["refresh"]);
        Assert.Equal("1", requested.Metadata["metadataCount"]);

        var persisted = Assert.Single(entries, entry => entry.Step == "evidence.cache.persisted");
        Assert.Equal("Persisted evidence cache manifest.", persisted.Message);
        Assert.Equal("/cache", persisted.Metadata["cacheDirectory"]);
        Assert.Equal(cacheResult.Manifest.Key, persisted.Metadata["cacheKey"]);
        Assert.Equal(
            cacheResult.Manifest.Artifacts.Count.ToString(CultureInfo.InvariantCulture),
            persisted.Metadata["artifactCount"]);
        Assert.Equal("Created", persisted.Metadata["cacheOutcome"]);
        Assert.Equal("None", persisted.Metadata["cacheReason"]);
        Assert.Equal("new", persisted.Metadata["reasonCode"]);
    }

    [Fact]
    public async Task CoordinateAsync_WhenCacheReused_RecordsReuseTelemetry()
    {
        var manifest = CreateManifest();
        var evaluationMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["reasonCode"] = "unchanged"
        };
        var evaluation = CreateEvaluation(
            EvidenceCacheOutcome.Reused,
            EvidenceCacheInvalidationReason.None,
            evaluationMetadata);
        var cacheResult = new EvidenceCacheResult("/cache", manifest, evaluation);
        var service = new StubEvidenceCacheService(Result<EvidenceCacheResult>.Success(cacheResult));
        var coordinator = new EvidenceCacheCoordinator(service);
        var log = new PipelineExecutionLogBuilder(TimeProvider.System);
        var options = new EvidenceCachePipelineOptions(
            "/cache-root",
            Refresh: false,
            Command: "dmm-compare",
            ModelPath: "model.json",
            ProfilePath: "profile.json",
            DmmPath: "baseline.sql",
            ConfigPath: null,
            Metadata: null);

        var coordination = await coordinator.CoordinateAsync(options, log);

        Assert.True(coordination.IsSuccess);
        Assert.Same(cacheResult, coordination.Value);

        var entries = log.Build().Entries;
        var requested = Assert.Single(entries, entry => entry.Step == "evidence.cache.requested");
        Assert.Equal("0", requested.Metadata["metadataCount"]);

        var reused = Assert.Single(entries, entry => entry.Step == "evidence.cache.reused");
        Assert.Equal("Reused evidence cache manifest.", reused.Message);
        Assert.Equal("Reused", reused.Metadata["cacheOutcome"]);
        Assert.Equal("None", reused.Metadata["cacheReason"]);
        Assert.Equal("unchanged", reused.Metadata["reasonCode"]);
    }

    [Fact]
    public async Task CoordinateAsync_WhenRefreshRequested_RecordsRefreshReason()
    {
        var manifest = CreateManifest();
        var evaluation = CreateEvaluation(
            EvidenceCacheOutcome.Created,
            EvidenceCacheInvalidationReason.RefreshRequested,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["reasonCode"] = "refresh"
            });
        var cacheResult = new EvidenceCacheResult("/cache", manifest, evaluation);
        var service = new StubEvidenceCacheService(Result<EvidenceCacheResult>.Success(cacheResult));
        var coordinator = new EvidenceCacheCoordinator(service);
        var log = new PipelineExecutionLogBuilder(TimeProvider.System);
        var options = new EvidenceCachePipelineOptions(
            "/cache-root",
            Refresh: true,
            Command: "build-ssdt",
            ModelPath: "model.json",
            ProfilePath: "profile.json",
            DmmPath: null,
            ConfigPath: null,
            Metadata: null);

        var coordination = await coordinator.CoordinateAsync(options, log);

        Assert.True(coordination.IsSuccess);
        Assert.True(service.LastRequest!.Refresh);

        var entries = log.Build().Entries;
        var persisted = Assert.Single(entries, entry => entry.Step == "evidence.cache.persisted");
        Assert.Equal("Persisted evidence cache manifest.", persisted.Message);
        Assert.Equal("RefreshRequested", persisted.Metadata["cacheReason"]);
        Assert.Equal("refresh", persisted.Metadata["reasonCode"]);
    }

    private static EvidenceCacheManifest CreateManifest()
    {
        return new EvidenceCacheManifest(
            Version: "1.0",
            Key: Guid.NewGuid().ToString("N"),
            Command: "build-ssdt",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastValidatedAtUtc: null,
            ExpiresAtUtc: null,
            ModuleSelection: EvidenceCacheModuleSelection.Empty,
            Metadata: new Dictionary<string, string?>(StringComparer.Ordinal),
            Artifacts: new[]
            {
                new EvidenceCacheArtifact(
                    EvidenceArtifactType.Model,
                    "model.json",
                    "model.json",
                    "hash",
                    42)
            });
    }

    private static EvidenceCacheEvaluation CreateEvaluation(
        EvidenceCacheOutcome outcome,
        EvidenceCacheInvalidationReason reason,
        IReadOnlyDictionary<string, string?> metadata)
    {
        return new EvidenceCacheEvaluation(outcome, reason, DateTimeOffset.UtcNow, metadata);
    }

    private sealed class StubEvidenceCacheService : IEvidenceCacheService
    {
        private readonly Result<EvidenceCacheResult> _result;

        public StubEvidenceCacheService(Result<EvidenceCacheResult> result)
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
