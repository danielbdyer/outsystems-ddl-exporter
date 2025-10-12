using Osm.Domain.Configuration;
using Osm.Validation.Tightening;
using Osm.Smo;
using Osm.Pipeline.Mediation;

namespace Osm.Pipeline.Orchestration;

public sealed record DmmComparePipelineRequest(
    string ModelPath,
    ModuleFilterOptions ModuleFilter,
    string ProfilePath,
    string DmmPath,
    TighteningOptions TighteningOptions,
    SupplementalModelOptions SupplementalModels,
    ResolvedSqlOptions SqlOptions,
    SmoBuildOptions SmoOptions,
    TypeMappingPolicy TypeMappingPolicy,
    string DiffOutputPath,
    EvidenceCachePipelineOptions? EvidenceCache) : ICommand<DmmComparePipelineResult>;
