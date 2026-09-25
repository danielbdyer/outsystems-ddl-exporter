using System;
using System.Collections.Generic;
using Estate.Budgets.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>io/Telemetry: a host that never calls Telemetry.OptOut is opted out by io's module initializer before any io type could load DacFx.</summary>
public sealed class TelemetryTests
{
    [Fact]
    [Trait("Category", "build")]
    [Trait("Value", "X5")]
    public void Loading_io_opts_out_of_telemetry_before_DacFx_loads()
    {
        var ran = new Command("dotnet", [typeof(EstateProcess).Assembly.Location, "telemetry"], Programs.Default)
        {
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["DACFX_TELEMETRY_OPTOUT"] = null, ["DOTNET_CLI_TELEMETRY_OPTOUT"] = null },
        }.Finish();

        Assert.Equal((0, "1 1 dacfx-not-loaded"), (ran.Code, ran.Output.Trim()));
    }
}
