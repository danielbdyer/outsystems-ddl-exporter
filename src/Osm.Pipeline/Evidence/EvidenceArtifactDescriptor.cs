namespace Osm.Pipeline.Evidence;

internal sealed record EvidenceArtifactDescriptor(
    EvidenceArtifactType Type,
    string SourcePath,
    string Hash,
    long Length,
    string Extension);
