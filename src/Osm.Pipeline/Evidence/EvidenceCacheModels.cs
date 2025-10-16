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
    DateTimeOffset? LastValidatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    EvidenceCacheModuleSelection? ModuleSelection,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<EvidenceCacheArtifact> Artifacts);

public sealed record EvidenceCacheResult(
    string CacheDirectory,
    EvidenceCacheManifest Manifest,
    EvidenceCacheEvaluation Evaluation);

public sealed record EvidenceCacheModuleSelection(
    bool IncludeSystemModules,
    bool IncludeInactiveModules,
    int ModuleCount,
    string? ModulesHash,
    IReadOnlyList<string> Modules)
{
    public static EvidenceCacheModuleSelection Empty { get; } = new(true, true, 0, null, Array.Empty<string>());
}

public enum EvidenceCacheOutcome
{
    Created,
    Reused
}

public enum EvidenceCacheInvalidationReason
{
    None,
    ManifestMissing,
    ManifestInvalid,
    ManifestVersionMismatch,
    KeyMismatch,
    CommandMismatch,
    ManifestExpired,
    ModuleSelectionChanged,
    MetadataMismatch,
    ArtifactsMismatch,
    RefreshRequested
}

public sealed record EvidenceCacheEvaluation(
    EvidenceCacheOutcome Outcome,
    EvidenceCacheInvalidationReason Reason,
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record EvidenceCacheRequest(
    string RootDirectory,
    string Command,
    string? ModelPath,
    string? ProfilePath,
    string? DmmPath,
    string? ConfigPath,
    IReadOnlyDictionary<string, string?> Metadata,
    bool Refresh,
    TimeSpan? RetentionMaxAge,
    int? RetentionMaxEntries);

public interface IEvidenceCacheService
{
    Task<Result<EvidenceCacheResult>> CacheAsync(
        EvidenceCacheRequest request,
        CancellationToken cancellationToken = default);
}
