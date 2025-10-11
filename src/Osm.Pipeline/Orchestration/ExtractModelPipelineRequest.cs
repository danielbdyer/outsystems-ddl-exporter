using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Mediation;

namespace Osm.Pipeline.Orchestration;

public sealed record ExtractModelPipelineRequest(
    ModelExtractionCommand Command,
    ResolvedSqlOptions SqlOptions,
    string? AdvancedSqlFixtureManifestPath) : ICommand<ModelExtractionResult>;
