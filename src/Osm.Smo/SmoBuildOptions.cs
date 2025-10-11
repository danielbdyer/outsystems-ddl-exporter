namespace Osm.Smo;

using System;
using Osm.Domain.Configuration;

public sealed record SmoBuildOptions(
    string DefaultCatalogName,
    bool IncludePlatformAutoIndexes,
    bool EmitBareTableOnly,
    bool SanitizeModuleNames,
    int ModuleParallelism,
    NamingOverrideOptions NamingOverrides,
    SmoFormatOptions Format)
{
    public static SmoBuildOptions Default { get; } = new(
        DefaultCatalogName: "OutSystems",
        IncludePlatformAutoIndexes: false,
        EmitBareTableOnly: false,
        SanitizeModuleNames: true,
        ModuleParallelism: 1,
        NamingOverrides: NamingOverrideOptions.Empty,
        Format: SmoFormatOptions.Default);

    public static SmoBuildOptions FromEmission(EmissionOptions emission, bool applyNamingOverrides = true) => new(
        DefaultCatalogName: "OutSystems",
        emission.IncludePlatformAutoIndexes,
        emission.EmitBareTableOnly,
        emission.SanitizeModuleNames,
        emission.ModuleParallelism,
        applyNamingOverrides ? emission.NamingOverrides : NamingOverrideOptions.Empty,
        SmoFormatOptions.Default);

    public SmoBuildOptions WithCatalog(string catalogName)
    {
        if (string.IsNullOrWhiteSpace(catalogName))
        {
            throw new ArgumentException("Catalog name must be provided.", nameof(catalogName));
        }

        return this with { DefaultCatalogName = catalogName.Trim() };
    }

    public SmoBuildOptions WithNamingOverrides(NamingOverrideOptions namingOverrides)
    {
        if (namingOverrides is null)
        {
            throw new ArgumentNullException(nameof(namingOverrides));
        }

        return this with { NamingOverrides = namingOverrides };
    }

    public SmoBuildOptions WithFormat(SmoFormatOptions format)
    {
        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return this with { Format = format };
    }
}
