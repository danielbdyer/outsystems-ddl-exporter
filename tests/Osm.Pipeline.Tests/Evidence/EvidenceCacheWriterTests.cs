using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Osm.Pipeline;
using System.Threading.Tasks;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class EvidenceCacheWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldPersistManifestAndArtifacts()
    {
        var fileSystem = new MockFileSystem();
        var canonicalizer = new ForwardSlashPathCanonicalizer();
        const string cacheDirectory = "/cache/abcd";
        const string modelPath = "/inputs/model.json";
        const string configPath = "/inputs/config.json";

        const string modelContent = "{\"model\":true}";
        const string configContent = "{\"config\":true}";

        fileSystem.AddFile(modelPath, new MockFileData(modelContent));
        fileSystem.AddFile(configPath, new MockFileData(configContent));

        var descriptors = new List<EvidenceArtifactDescriptor>
        {
            CreateDescriptor(EvidenceArtifactType.Model, modelPath, modelContent, ".json"),
            CreateDescriptor(EvidenceArtifactType.Configuration, configPath, configContent, ".json")
        };

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["cache.ttlSeconds"] = "3600"
        };

        var writer = new EvidenceCacheWriter(fileSystem, canonicalizer);
        var creationTimestamp = new DateTimeOffset(2024, 08, 06, 10, 00, 00, TimeSpan.Zero);
        var manifest = await writer.WriteAsync(
            cacheDirectory,
            "manifest.json",
            "1.0",
            "abcd",
            "build",
            creationTimestamp,
            EvidenceCacheModuleSelection.Empty,
            metadata,
            descriptors,
            CancellationToken.None);

        Assert.Equal("1.0", manifest.Version);
        Assert.Equal("abcd", manifest.Key);
        Assert.Equal(creationTimestamp.AddHours(1), manifest.ExpiresAtUtc);
        Assert.Equal(2, manifest.Artifacts.Count);
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDirectory, "manifest.json")));
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDirectory, "model.json")));
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDirectory, "config.json")));

        var persistedManifest = JsonSerializer.Deserialize<EvidenceCacheManifest>(
            fileSystem.File.ReadAllText(fileSystem.Path.Combine(cacheDirectory, "manifest.json")));
        Assert.NotNull(persistedManifest);
        Assert.Equal(manifest.Key, persistedManifest!.Key);
        Assert.Equal("build", persistedManifest.Command);
        Assert.Equal(creationTimestamp.AddHours(1), persistedManifest.ExpiresAtUtc);
    }

    [Fact]
    public async Task WriteAsync_ShouldDeriveFileNameFromDescriptorType()
    {
        var fileSystem = new MockFileSystem();
        const string cacheDirectory = "/cache/dmm";
        const string dmmPath = "/inputs/dmm";
        const string content = "SELECT 1";

        fileSystem.AddFile(dmmPath, new MockFileData(content));

        var descriptor = CreateDescriptor(EvidenceArtifactType.Dmm, dmmPath, content, ".sql");
        var canonicalizer = new ForwardSlashPathCanonicalizer();
        var writer = new EvidenceCacheWriter(fileSystem, canonicalizer);
        var manifest = await writer.WriteAsync(
            cacheDirectory,
            "manifest.json",
            "1.0",
            "dmm",
            "build",
            new DateTimeOffset(2024, 08, 07, 09, 00, 00, TimeSpan.Zero),
            EvidenceCacheModuleSelection.Empty,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            new[] { descriptor },
            CancellationToken.None);

        var artifact = Assert.Single(manifest.Artifacts);
        Assert.Equal("dmm.sql", artifact.RelativePath);
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(cacheDirectory, artifact.RelativePath)));
    }

    private static EvidenceArtifactDescriptor CreateDescriptor(
        EvidenceArtifactType type,
        string sourcePath,
        string content,
        string extension)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new EvidenceArtifactDescriptor(type, sourcePath, hash, bytes.Length, extension);
    }
}
