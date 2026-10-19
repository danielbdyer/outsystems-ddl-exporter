using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DbChange.Tests;

/// <summary>
/// DacFx's telemetry off before any test class loads DacFx: the runner starts a test assembly's classes in an order it chooses, some
/// of them loading DacFx types before any io type, and in a test assembly only a module initializer runs before all of them, as
/// dbchange's Main opts out before its first DacFx load.
/// </summary>
internal static class TelemetryOptOut
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "A test assembly is a library to the runner, and only a module initializer runs before every test class.")]
    internal static void BeforeAnyClass() => DbChange.Io.Telemetry.OptOut();
}
