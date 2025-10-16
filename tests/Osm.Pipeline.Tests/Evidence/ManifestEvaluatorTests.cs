using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class ManifestEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_ShouldReuseManifest_WhenArtifactsMatch()
    {
        var fileSystem = new MockFileSystem();
        var cacheDirectory = "/cache/abcd";
        fileSystem.AddDirectory(cacheDirectory);

        const string sourcePath = "/inputs/model.json";
        const string command = "build";
        const string key = "abcd";
        const string content = "{\"model\":true}";

        fileSystem.AddFile(sourcePath, new MockFileData(content));
        fileSystem.AddFile(fileSystem.Path.Combine(cacheDirectory, "model.json"), new MockFileData(content));

        var descriptor = CreateDescriptor(EvidenceArtifactType.Model, sourcePath, content);
        var manifest = new EvidenceCacheManifest(
            "1.0",
            key,
            command,
            new DateTimeOffset(2024, 08, 01, 10, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 01, 10, 00, 00, TimeSpan.Zero),
            null,
            EvidenceCacheModuleSelection.Empty,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            new List<EvidenceCacheArtifact>
            {
                new(descriptor.Type, descriptor.SourcePath, "model.json", descriptor.Hash, descriptor.Length)
            });

        WriteManifest(fileSystem, cacheDirectory, manifest);

        var evaluator = new ManifestEvaluator(fileSystem);
        var evaluationTimestamp = new DateTimeOffset(2024, 08, 01, 12, 00, 00, TimeSpan.Zero);
        var evaluation = await evaluator.EvaluateAsync(
            cacheDirectory,
            "manifest.json",
            "1.0",
            key,
            command,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            EvidenceCacheModuleSelection.Empty,
            new[] { descriptor },
            evaluationTimestamp,
            CancellationToken.None);

        Assert.Equal(EvidenceCacheOutcome.Reused, evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.None, evaluation.Reason);
        Assert.NotNull(evaluation.Manifest);
        Assert.Equal(evaluationTimestamp, evaluation.Manifest!.LastValidatedAtUtc);
        Assert.Equal("cache.reused", evaluation.Metadata["reason"]);
    }

    [Fact]
    public async Task EvaluateAsync_ShouldDetectExpiredManifest()
    {
        var fileSystem = new MockFileSystem();
        var cacheDirectory = "/cache/expired";
        fileSystem.AddDirectory(cacheDirectory);

        const string sourcePath = "/inputs/model.json";
        const string command = "build";
        const string key = "expired";
        const string content = "{\"model\":true}";

        fileSystem.AddFile(sourcePath, new MockFileData(content));
        fileSystem.AddFile(fileSystem.Path.Combine(cacheDirectory, "model.json"), new MockFileData(content));

        var descriptor = CreateDescriptor(EvidenceArtifactType.Model, sourcePath, content);
        var createdAt = new DateTimeOffset(2024, 08, 02, 08, 00, 00, TimeSpan.Zero);
        var manifest = new EvidenceCacheManifest(
            "1.0",
            key,
            command,
            createdAt,
            createdAt,
            createdAt.AddHours(1),
            EvidenceCacheModuleSelection.Empty,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            new List<EvidenceCacheArtifact>
            {
                new(descriptor.Type, descriptor.SourcePath, "model.json", descriptor.Hash, descriptor.Length)
            });

        WriteManifest(fileSystem, cacheDirectory, manifest);

        var evaluator = new ManifestEvaluator(fileSystem);
        var evaluation = await evaluator.EvaluateAsync(
            cacheDirectory,
            "manifest.json",
            "1.0",
            key,
            command,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            EvidenceCacheModuleSelection.Empty,
            new[] { descriptor },
            createdAt.AddHours(3),
            CancellationToken.None);

        Assert.Equal(EvidenceCacheOutcome.Created, evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.ManifestExpired, evaluation.Reason);
        Assert.Equal("manifest.expired", evaluation.Metadata["reason"]);
        Assert.Equal(createdAt.AddHours(1).ToString("O"), evaluation.Metadata["expiresAtUtc"]);
    }

    [Fact]
    public async Task EvaluateAsync_ShouldDetectMetadataMismatch()
    {
        var fileSystem = new MockFileSystem();
        var cacheDirectory = "/cache/metadata";
        fileSystem.AddDirectory(cacheDirectory);

        const string sourcePath = "/inputs/model.json";
        const string command = "build";
        const string key = "metadata";
        const string content = "{\"model\":true}";

        fileSystem.AddFile(sourcePath, new MockFileData(content));
        fileSystem.AddFile(fileSystem.Path.Combine(cacheDirectory, "model.json"), new MockFileData(content));

        var descriptor = CreateDescriptor(EvidenceArtifactType.Model, sourcePath, content);
        var manifestMetadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["policy.mode"] = "EvidenceGated"
        };

        var manifest = new EvidenceCacheManifest(
            "1.0",
            key,
            command,
            new DateTimeOffset(2024, 08, 03, 07, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 03, 07, 00, 00, TimeSpan.Zero),
            null,
            EvidenceCacheModuleSelection.Empty,
            manifestMetadata,
            new List<EvidenceCacheArtifact>
            {
                new(descriptor.Type, descriptor.SourcePath, "model.json", descriptor.Hash, descriptor.Length)
            });

        WriteManifest(fileSystem, cacheDirectory, manifest);

        var evaluator = new ManifestEvaluator(fileSystem);
        var evaluation = await evaluator.EvaluateAsync(
            cacheDirectory,
            "manifest.json",
            "1.0",
            key,
            command,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["policy.mode"] = "Aggressive"
            },
            EvidenceCacheModuleSelection.Empty,
            new[] { descriptor },
            new DateTimeOffset(2024, 08, 03, 09, 00, 00, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(EvidenceCacheOutcome.Created, evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.MetadataMismatch, evaluation.Reason);
        Assert.Equal("metadata.mismatch", evaluation.Metadata["reason"]);
    }

    [Fact]
    public async Task EvaluateAsync_ShouldDescribeArtifactMismatch()
    {
        var fileSystem = new MockFileSystem();
        var cacheDirectory = "/cache/artifact";
        fileSystem.AddDirectory(cacheDirectory);

        const string sourcePath = "/inputs/model.json";
        const string command = "build";
        const string key = "artifact";
        const string content = "{\"model\":true}";

        fileSystem.AddFile(sourcePath, new MockFileData(content));
        fileSystem.AddFile(fileSystem.Path.Combine(cacheDirectory, "model.json"), new MockFileData(content));

        var descriptor = CreateDescriptor(EvidenceArtifactType.Model, sourcePath, content);
        var manifest = new EvidenceCacheManifest(
            "1.0",
            key,
            command,
            new DateTimeOffset(2024, 08, 04, 11, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 08, 04, 11, 00, 00, TimeSpan.Zero),
            null,
            EvidenceCacheModuleSelection.Empty,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            new List<EvidenceCacheArtifact>
            {
                new(descriptor.Type, descriptor.SourcePath, "model.json", descriptor.Hash, descriptor.Length)
            });

        WriteManifest(fileSystem, cacheDirectory, manifest);
        fileSystem.File.Delete(fileSystem.Path.Combine(cacheDirectory, "model.json"));

        var evaluator = new ManifestEvaluator(fileSystem);
        var evaluation = await evaluator.EvaluateAsync(
            cacheDirectory,
            "manifest.json",
            "1.0",
            key,
            command,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            EvidenceCacheModuleSelection.Empty,
            new[] { descriptor },
            new DateTimeOffset(2024, 08, 04, 12, 00, 00, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(EvidenceCacheOutcome.Created, evaluation.Outcome);
        Assert.Equal(EvidenceCacheInvalidationReason.ArtifactsMismatch, evaluation.Reason);
        Assert.Equal("path.missing", evaluation.Metadata["artifactMismatch.reason"]);
        Assert.Equal("artifacts.mismatch", evaluation.Metadata["reason"]);
    }

    private static EvidenceArtifactDescriptor CreateDescriptor(
        EvidenceArtifactType type,
        string sourcePath,
        string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new EvidenceArtifactDescriptor(type, sourcePath, hash, bytes.Length, ".json");
    }

    private static void WriteManifest(MockFileSystem fileSystem, string cacheDirectory, EvidenceCacheManifest manifest)
    {
        var manifestPath = fileSystem.Path.Combine(cacheDirectory, "manifest.json");
        fileSystem.AddFile(manifestPath, new MockFileData(string.Empty));
        fileSystem.File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
