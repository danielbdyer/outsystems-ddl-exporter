using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Orchestration;

public sealed record ExtractModelPipelineRequest(
    ModelExtractionCommand Command,
    ResolvedSqlOptions SqlOptions,
    string? AdvancedSqlFixtureManifestPath);
