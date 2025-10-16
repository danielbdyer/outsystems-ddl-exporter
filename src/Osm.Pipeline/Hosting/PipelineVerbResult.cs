using System.Collections.Generic;

namespace Osm.Pipeline.Hosting;

public sealed record PipelineVerbResult(int ExitCode, PipelineRun? Run = null, IReadOnlyCollection<ValidationMessage>? Messages = null);

public sealed record PipelineRun(string Verb, DateTimeOffset StartedUtc, DateTimeOffset CompletedUtc, IReadOnlyList<ArtifactRef> Artifacts);

public sealed record ArtifactRef(string Name, string Path);

public sealed record ValidationMessage(string Code, string Message);
