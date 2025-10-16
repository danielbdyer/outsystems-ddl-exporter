using System;
using System.Collections.Generic;

namespace Osm.Pipeline.Orchestration;

public sealed record EvidenceCachePipelineOptions(
    string? RootDirectory,
    bool Refresh,
    string Command,
    string ModelPath,
    string? ProfilePath,
    string? DmmPath,
    string? ConfigPath,
    IReadOnlyDictionary<string, string?>? Metadata,
    TimeSpan? RetentionMaxAge,
    int? RetentionMaxEntries);
