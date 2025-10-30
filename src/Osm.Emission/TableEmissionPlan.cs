using Osm.Domain.Model.Artifacts;

namespace Osm.Emission;

public sealed record TableEmissionPlan(
    TableArtifactSnapshot Snapshot,
    string Path,
    string Script);
