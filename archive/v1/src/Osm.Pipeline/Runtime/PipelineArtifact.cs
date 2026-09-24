namespace Osm.Pipeline.Runtime;

/// <summary>
/// Describes an artifact emitted by a pipeline run.
/// </summary>
/// <param name="Name">Logical artifact name.</param>
/// <param name="Path">Filesystem path to the artifact.</param>
/// <param name="ContentType">Optional MIME-style content type for downstream tooling.</param>
public sealed record PipelineArtifact(string Name, string Path, string? ContentType = null);
