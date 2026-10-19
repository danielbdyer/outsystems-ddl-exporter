using System.Collections.Generic;

namespace Osm.Pipeline.Evidence;

internal sealed record CacheRequestContext(
    string NormalizedRootDirectory,
    string CacheDirectory,
    string Command,
    string Key,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyCollection<EvidenceArtifactDescriptor> Descriptors,
    EvidenceCacheModuleSelection ModuleSelection,
    bool Refresh);
