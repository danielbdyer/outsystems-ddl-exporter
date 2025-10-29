namespace Osm.Emission;

public sealed record TableEmissionPlan(
    TableManifestEntry ManifestEntry,
    string Path,
    string Script);
