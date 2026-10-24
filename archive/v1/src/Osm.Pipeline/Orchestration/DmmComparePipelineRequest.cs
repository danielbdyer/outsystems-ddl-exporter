using Osm.Domain.Configuration;
using Osm.Validation.Tightening;
using Osm.Smo;
using Osm.Pipeline.Mediation;

namespace Osm.Pipeline.Orchestration;

public sealed record DmmComparePipelineRequest(
    ModelExecutionScope Scope,
    string DmmPath,
    string DiffOutputPath,
    EvidenceCachePipelineOptions? EvidenceCache) : ICommand<DmmComparePipelineResult>;
