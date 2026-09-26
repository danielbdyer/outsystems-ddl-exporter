using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.TestSupport;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Integration.Tests;

public sealed class EvidenceCacheCoordinatorIntegrationTests
{
    private static readonly string ModelPath = FixtureFile.GetPath("model.edge-case.json");
    private static readonly string ProfilePath = FixtureFile.GetPath("profiling/profile.edge-case.json");

    [Fact]
    public async Task CacheAsync_ShouldCreateAndReuseCacheEntries()
    {
        using var temp = new TempDirectory();
        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 05, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 05, 08, 30, 00, TimeSpan.Zero)
        });

        var service = new EvidenceCacheService(new FileSystem(), () => timestamps.Dequeue());
        var coordinator = new EvidenceCacheCoordinator(service);
        var options = CreateCacheOptions(
            temp.Path,
            modules: new[] { "AppCore", "ExtBilling" },
            includeSystem: false,
            includeInactive: true,
            timeToLiveSeconds: 7200);

        var firstLog = new PipelineExecutionLogBuilder();
        var first = await coordinator.CacheAsync(options, firstLog, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value.Should().NotBeNull();
        first.Value!.Evaluation.Outcome.Should().Be(EvidenceCacheOutcome.Created);
        first.Value.Evaluation.Reason.Should().Be(EvidenceCacheInvalidationReason.ManifestMissing);
        Directory.Exists(first.Value.CacheDirectory).Should().BeTrue();

        var firstEntries = firstLog.Build().Entries;
        firstEntries.Select(entry => entry.Step).Should().Contain(new[]
        {
            "evidence.cache.requested",
            "evidence.cache.persisted"
        });

        var secondLog = new PipelineExecutionLogBuilder();
        var second = await coordinator.CacheAsync(options, secondLog, CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.Should().NotBeNull();
        second.Value!.Evaluation.Outcome.Should().Be(EvidenceCacheOutcome.Reused);
        second.Value.Evaluation.Reason.Should().Be(EvidenceCacheInvalidationReason.None);
        second.Value.CacheDirectory.Should().Be(first.Value.CacheDirectory);

        var secondEntries = secondLog.Build().Entries;
        secondEntries.Select(entry => entry.Step).Should().Contain(new[]
        {
            "evidence.cache.requested",
            "evidence.cache.reused"
        });
    }

    [Fact]
    public async Task CacheAsync_ShouldExpireEntries_WhenTtlElapsed()
    {
        using var temp = new TempDirectory();
        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 06, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 06, 10, 05, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 06, 10, 10, 00, TimeSpan.Zero)
        });

        var service = new EvidenceCacheService(new FileSystem(), () => timestamps.Dequeue());
        var coordinator = new EvidenceCacheCoordinator(service);
        var options = CreateCacheOptions(
            temp.Path,
            modules: new[] { "AppCore" },
            includeSystem: false,
            includeInactive: true,
            timeToLiveSeconds: 3600);

        var firstLog = new PipelineExecutionLogBuilder();
        var first = await coordinator.CacheAsync(options, firstLog, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();
        var firstManifest = first.Value!.Manifest;
        firstManifest.ExpiresAtUtc.Should().Be(new DateTimeOffset(2024, 08, 06, 09, 00, 00, TimeSpan.Zero));

        var secondLog = new PipelineExecutionLogBuilder();
        var second = await coordinator.CacheAsync(options, secondLog, CancellationToken.None);
        second.IsSuccess.Should().BeTrue();
        second.Value!.Evaluation.Outcome.Should().Be(EvidenceCacheOutcome.Created);
        second.Value.Evaluation.Reason.Should().Be(EvidenceCacheInvalidationReason.ManifestExpired);
        second.Value.Manifest.CreatedAtUtc.Should().Be(new DateTimeOffset(2024, 08, 06, 10, 10, 00, TimeSpan.Zero));

        var secondEntries = secondLog.Build().Entries;
        var persisted = secondEntries.Single(entry => entry.Step == "evidence.cache.persisted");
        persisted.Metadata.Should().ContainKey("evaluation.reason");
        persisted.Metadata["evaluation.reason"].Should().Be("manifest.expired");
    }

    [Fact]
    public async Task CacheAsync_ShouldEvictCache_WhenMetadataChanges()
    {
        using var temp = new TempDirectory();
        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 07, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 07, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 07, 09, 05, 00, TimeSpan.Zero)
        });

        var service = new EvidenceCacheService(new FileSystem(), () => timestamps.Dequeue());
        var coordinator = new EvidenceCacheCoordinator(service);
        var baselineOptions = CreateCacheOptions(
            temp.Path,
            modules: new[] { "AppCore" },
            includeSystem: false,
            includeInactive: true,
            timeToLiveSeconds: 7200,
            tightening: TighteningOptions.Default);

        var baselineLog = new PipelineExecutionLogBuilder();
        var baseline = await coordinator.CacheAsync(baselineOptions, baselineLog, CancellationToken.None);
        baseline.IsSuccess.Should().BeTrue();
        var baselineDirectory = baseline.Value!.CacheDirectory;
        Directory.Exists(baselineDirectory).Should().BeTrue();

        var aggressivePolicy = PolicyOptions.Create(TighteningMode.Aggressive, TighteningOptions.Default.Policy.NullBudget).Value;
        var aggressiveOptions = TighteningOptions.Create(
            aggressivePolicy,
            TighteningOptions.Default.ForeignKeys,
            TighteningOptions.Default.Uniqueness,
            TighteningOptions.Default.Remediation,
            TighteningOptions.Default.Emission,
            TighteningOptions.Default.Mocking).Value;

        var refreshedOptions = CreateCacheOptions(
            temp.Path,
            modules: new[] { "AppCore" },
            includeSystem: false,
            includeInactive: true,
            timeToLiveSeconds: 7200,
            tightening: aggressiveOptions);

        var refreshedLog = new PipelineExecutionLogBuilder();
        var refreshed = await coordinator.CacheAsync(refreshedOptions, refreshedLog, CancellationToken.None);
        refreshed.IsSuccess.Should().BeTrue();
        refreshed.Value!.Evaluation.Reason.Should().Be(EvidenceCacheInvalidationReason.MetadataMismatch);

        Directory.Exists(baselineDirectory).Should().BeFalse();
        Directory.Exists(refreshed.Value.CacheDirectory).Should().BeTrue();

        var persisted = refreshedLog.Build().Entries.Single(entry => entry.Step == "evidence.cache.persisted");
        persisted.Metadata.Should().Contain(new KeyValuePair<string, string?>("evaluation.reason", "metadata.mismatch"));
    }

    [Fact]
    public async Task CacheAsync_ShouldRemoveSupersededEntries_WhenModuleSelectionShrinks()
    {
        using var temp = new TempDirectory();
        var timestamps = new Queue<DateTimeOffset>(new[]
        {
            new DateTimeOffset(2024, 08, 08, 08, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 08, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 08, 09, 05, 00, TimeSpan.Zero)
        });

        var service = new EvidenceCacheService(new FileSystem(), () => timestamps.Dequeue());
        var coordinator = new EvidenceCacheCoordinator(service);
        var supersetOptions = CreateCacheOptions(
            temp.Path,
            modules: new[] { "AppCore", "ExtBilling" },
            includeSystem: false,
            includeInactive: true,
            timeToLiveSeconds: 7200);

        var supersetLog = new PipelineExecutionLogBuilder();
        var superset = await coordinator.CacheAsync(supersetOptions, supersetLog, CancellationToken.None);
        superset.IsSuccess.Should().BeTrue();
        var supersetDirectory = superset.Value!.CacheDirectory;
        Directory.Exists(supersetDirectory).Should().BeTrue();

        var subsetOptions = CreateCacheOptions(
            temp.Path,
            modules: new[] { "AppCore" },
            includeSystem: false,
            includeInactive: true,
            timeToLiveSeconds: 7200);

        var subsetLog = new PipelineExecutionLogBuilder();
        var subset = await coordinator.CacheAsync(subsetOptions, subsetLog, CancellationToken.None);
        subset.IsSuccess.Should().BeTrue();
        subset.Value!.Evaluation.Reason.Should().Be(EvidenceCacheInvalidationReason.ModuleSelectionChanged);
        Directory.Exists(supersetDirectory).Should().BeFalse();
        Directory.Exists(subset.Value.CacheDirectory).Should().BeTrue();

        var persisted = subsetLog.Build().Entries.Single(entry => entry.Step == "evidence.cache.persisted");
        persisted.Metadata.Should().Contain(new KeyValuePair<string, string?>("evaluation.reason", "module.selection.changed"));
    }

    private static EvidenceCachePipelineOptions CreateCacheOptions(
        string cacheRoot,
        IReadOnlyList<string> modules,
        bool includeSystem,
        bool includeInactive,
        int timeToLiveSeconds,
        TighteningOptions? tightening = null)
    {
        tightening ??= TighteningOptions.Default;

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["policy.mode"] = tightening.Policy.Mode.ToString(),
            ["policy.nullBudget"] = tightening.Policy.NullBudget.ToString(CultureInfo.InvariantCulture),
            ["foreignKeys.enableCreation"] = tightening.ForeignKeys.EnableCreation.ToString(),
            ["foreignKeys.allowCrossSchema"] = tightening.ForeignKeys.AllowCrossSchema.ToString(),
            ["foreignKeys.allowCrossCatalog"] = tightening.ForeignKeys.AllowCrossCatalog.ToString(),
            ["foreignKeys.treatMissingDeleteRuleAsIgnore"] = tightening.ForeignKeys.TreatMissingDeleteRuleAsIgnore.ToString(),
            ["uniqueness.singleColumn"] = tightening.Uniqueness.EnforceSingleColumnUnique.ToString(),
            ["uniqueness.multiColumn"] = tightening.Uniqueness.EnforceMultiColumnUnique.ToString(),
            ["cache.ttlSeconds"] = timeToLiveSeconds.ToString(CultureInfo.InvariantCulture),
            ["sql.commandTimeoutSeconds"] = "60",
            ["sql.sampling.rowThreshold"] = "250000",
            ["sql.sampling.sampleSize"] = "50000"
        };

        metadata["moduleFilter.includeSystemModules"] = includeSystem.ToString();
        metadata["moduleFilter.includeInactiveModules"] = includeInactive.ToString();

        var normalizedModules = modules
            .Where(module => !string.IsNullOrWhiteSpace(module))
            .Select(module => module.Trim())
            .OrderBy(module => module, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        metadata["moduleFilter.moduleCount"] = normalizedModules.Length.ToString(CultureInfo.InvariantCulture);

        if (normalizedModules.Length > 0)
        {
            metadata["moduleFilter.modules"] = string.Join(",", normalizedModules);
            metadata["moduleFilter.modulesHash"] = ComputeSha256(string.Join(";", normalizedModules));
            metadata["moduleFilter.selectionScope"] = "filtered";
        }
        else
        {
            metadata["moduleFilter.modulesHash"] = ComputeSha256("::all-modules::");
            metadata["moduleFilter.selectionScope"] = "all";
        }

        return new EvidenceCachePipelineOptions(
            cacheRoot,
            Refresh: false,
            Command: "build-ssdt",
            ModelPath,
            ProfilePath,
            DmmPath: null,
            ConfigPath: null,
            metadata);
    }

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
