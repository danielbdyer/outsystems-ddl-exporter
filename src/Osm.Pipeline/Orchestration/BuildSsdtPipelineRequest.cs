using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Orchestration;

public sealed record BuildSsdtPipelineRequest(
    string ModelPath,
    ModuleFilterOptions ModuleFilter,
    string OutputDirectory,
    TighteningOptions TighteningOptions,
    SupplementalModelOptions SupplementalModels,
    string ProfilerProvider,
    string? ProfilePath,
    ResolvedSqlOptions SqlOptions,
    SmoBuildOptions SmoOptions,
    TypeMappingPolicy TypeMappingPolicy,
    EvidenceCachePipelineOptions? EvidenceCache,
    IStaticEntityDataProvider? StaticDataProvider,
    string? SeedOutputDirectoryHint,
    SqlMetadataLog? SqlMetadataLog = null) : ICommand<BuildSsdtPipelineResult>;
