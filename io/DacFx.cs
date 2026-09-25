using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;

namespace Estate.Io;

/// <summary>
/// The one adapter to DacFx's runtime surface (DacServices and DacProfile): the release estate runs, made once.
/// </summary>
public static class DacFx
{
    /// <summary>
    /// The DacFx release estate runs, made once from Microsoft.SqlServer.Dac.dll: its file version as major.minor.build (170.5.96), or, where
    /// the assembly has no file on disk (a single-file or bundled host), its informational version before the '+'; toolchain.dacfx-version
    /// when it carries neither. doctor reports that error as an item, and every other verb answers it before any work.
    /// </summary>
    public static Result<DacFxVersion> Version { get; } = VersionOf(typeof(DacServices).Assembly.Location,
        typeof(DacServices).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The release from the assembly's path and its informational version, as <see cref="Version"/> reads them.</summary>
    internal static Result<DacFxVersion> VersionOf(string location, string? informational) =>
        location.Length > 0 && File.Exists(location) && FileVersionInfo.GetVersionInfo(location) is { FileVersion: not null } file
            ? DacFxVersion.Of(string.Create(CultureInfo.InvariantCulture, $"{file.FileMajorPart}.{file.FileMinorPart}.{file.FileBuildPart}"))
        : informational?.Split('+')[0] is { Length: > 0 } text ? DacFxVersion.Of(text)
        : new Error("toolchain.dacfx-version", "Microsoft.SqlServer.Dac.dll carries no file version and no informational version, so estate cannot name the DacFx release it runs.",
            "Run estate from a tool folder ci/publish wrote, whose DacFx assemblies carry their versions.");
}
