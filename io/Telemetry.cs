using System;

namespace Estate.Io;

/// <summary>
/// The engine sends nothing home. DacFx reads DACFX_TELEMETRY_OPTOUT when it loads and the .NET CLI reads
/// DOTNET_CLI_TELEMETRY_OPTOUT when it starts, so the opt-out is the first statement of estate's Main, and
/// every process estate starts (dotnet build, MSBuild's DacFx task) inherits it.
/// </summary>
public static class Telemetry
{
    public static void OptOut()
    {
        Environment.SetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT", "1");
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
    }
}
