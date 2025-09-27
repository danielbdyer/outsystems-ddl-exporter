namespace Osm.Smo;

using System;
using Osm.Domain.Configuration;

public sealed record SmoBuildOptions(
    string DefaultCatalogName,
    bool IncludePlatformAutoIndexes,
    bool EmitConcatenatedConstraints,
    bool SanitizeModuleNames)
{
    public static SmoBuildOptions Default { get; } = new(
        DefaultCatalogName: "OutSystems",
        IncludePlatformAutoIndexes: false,
        EmitConcatenatedConstraints: false,
        SanitizeModuleNames: true);

    public static SmoBuildOptions FromEmission(EmissionOptions emission) => new(
        DefaultCatalogName: "OutSystems",
        emission.IncludePlatformAutoIndexes,
        emission.EmitConcatenatedConstraints,
        emission.SanitizeModuleNames);

    public SmoBuildOptions WithCatalog(string catalogName)
    {
        if (string.IsNullOrWhiteSpace(catalogName))
        {
            throw new ArgumentException("Catalog name must be provided.", nameof(catalogName));
        }

        return this with { DefaultCatalogName = catalogName.Trim() };
    }
}
