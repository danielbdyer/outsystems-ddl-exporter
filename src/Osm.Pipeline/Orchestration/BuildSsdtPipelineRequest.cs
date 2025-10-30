using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Orchestration;

public sealed record BuildSsdtPipelineRequest(
    ModelExecutionScope Scope,
    string OutputDirectory,
    string ProfilerProvider,
    EvidenceCachePipelineOptions? EvidenceCache,
    IStaticEntityDataProvider? StaticDataProvider,
    string? SeedOutputDirectoryHint,
    SqlMetadataLog? SqlMetadataLog = null) : ICommand<BuildSsdtPipelineResult>;
