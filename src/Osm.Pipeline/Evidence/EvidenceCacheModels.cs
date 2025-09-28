using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Evidence;

public sealed record EvidenceCacheArtifact(
    EvidenceArtifactType Type,
    string OriginalPath,
    string RelativePath,
    string Hash,
    long Length);

public sealed record EvidenceCacheManifest(
    string Version,
    string Key,
    string Command,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<EvidenceCacheArtifact> Artifacts);

public sealed record EvidenceCacheResult(
    string CacheDirectory,
    EvidenceCacheManifest Manifest);

public sealed record EvidenceCacheRequest(
    string RootDirectory,
    string Command,
    string? ModelPath,
    string? ProfilePath,
    string? DmmPath,
    string? ConfigPath,
    IReadOnlyDictionary<string, string?> Metadata,
    bool Refresh);

public interface IEvidenceCacheService
{
    Task<Result<EvidenceCacheResult>> CacheAsync(
        EvidenceCacheRequest request,
        CancellationToken cancellationToken = default);
}
