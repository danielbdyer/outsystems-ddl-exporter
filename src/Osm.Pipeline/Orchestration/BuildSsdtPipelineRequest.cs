using Osm.Domain.Configuration;
using Osm.Emission;
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
    DynamicEntityDataset DynamicDataset,
    IStaticEntityDataProvider? StaticDataProvider,
    string? SeedOutputDirectoryHint,
    string? DynamicDataOutputDirectoryHint,
    string? SqlProjectPathHint,
    SqlMetadataLog? SqlMetadataLog = null) : ICommand<BuildSsdtPipelineResult>;
