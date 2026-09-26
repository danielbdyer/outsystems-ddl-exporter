namespace Osm.Emission.Seeds;

public static class EntityDependencyOrderingModeExtensions
{
    public static string ToMetadataValue(this EntityDependencyOrderingMode mode)
    {
        return mode switch
        {
            EntityDependencyOrderingMode.Topological => "topological",
            EntityDependencyOrderingMode.JunctionDeferred => "junction-deferred",
            _ => "alphabetical"
        };
    }
}
