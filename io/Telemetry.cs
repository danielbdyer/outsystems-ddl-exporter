using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DbChange.Io;

/// <summary>
/// dbchange sends nothing home. DacFx reads DACFX_TELEMETRY_OPTOUT when it loads and the .NET CLI reads
/// DOTNET_CLI_TELEMETRY_OPTOUT when it starts, so the opt-out is the first statement of dbchange's Main, and
/// every process dbchange starts (dotnet build, MSBuild's DacFx task) inherits it. A host other than cli (a test
/// class, a process that drives io) is opted out too: io's module initializer runs before any io type is used,
/// and no io type touches DacFx before that.
/// </summary>
public static class Telemetry
{
    public static void OptOut()
    {
        Environment.SetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT", "1");
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
    }

    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "The opt-out must precede every host's first DacFx load, and io is the library every host loads before DacFx.")]
    internal static void Initialize() => OptOut();
}
