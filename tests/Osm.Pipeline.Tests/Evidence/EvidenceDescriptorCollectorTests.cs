using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline;
using Osm.Pipeline.Evidence;

namespace Osm.Pipeline.Tests.Evidence;

public sealed class EvidenceDescriptorCollectorTests
{
    [Fact]
    public async Task CollectAsync_ShouldDescribeExistingArtifacts()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/inputs/model.json"] = new MockFileData("{\"model\":true}"),
            ["/inputs/profile.json"] = new MockFileData("{\"profile\":true}"),
            ["/inputs/dmm"] = new MockFileData("SELECT 1;"),
        });

        var canonicalizer = new ForwardSlashPathCanonicalizer();
        var collector = new EvidenceDescriptorCollector(fileSystem, canonicalizer);
        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build",
            ModelPath: "/inputs/model.json",
            ProfilePath: "/inputs/profile.json",
            DmmPath: "/inputs/dmm",
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>(),
            Refresh: false);

        var result = await collector.CollectAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var descriptors = result.Value;
        Assert.Equal(3, descriptors.Count);

        var dmmDescriptor = Assert.Single(descriptors.Where(static d => d.Type == EvidenceArtifactType.Dmm));
        Assert.Equal(".sql", dmmDescriptor.Extension);
        Assert.Equal(Encoding.UTF8.GetByteCount("SELECT 1;"), dmmDescriptor.Length);
        Assert.All(descriptors, descriptor => Assert.False(string.IsNullOrWhiteSpace(descriptor.Hash)));
    }

    [Fact]
    public async Task CollectAsync_ShouldReturnFailure_WhenModelMissing()
    {
        var fileSystem = new MockFileSystem();
        var collector = new EvidenceDescriptorCollector(fileSystem, new ForwardSlashPathCanonicalizer());
        var request = new EvidenceCacheRequest(
            RootDirectory: "/cache",
            Command: "build",
            ModelPath: "/inputs/model.json",
            ProfilePath: null,
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>(),
            Refresh: false);

        var result = await collector.CollectAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("cache.model.notFound", error.Code);
    }
}
