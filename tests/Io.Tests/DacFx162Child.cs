using System;
using System.Collections.Generic;
using System.IO;
using DbChange.Budgets.Tests;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// DacFx 162.5.57, the release the pipeline is presumed to run, as the child process tests/DacFx162/ builds (DECISIONS.md, 2026-10-19):
/// <c>dotnet tests/DacFx162/bin/&lt;configuration&gt;/&lt;framework&gt;/DbChange.DacFx162.dll &lt;verb&gt; …</c>, the configuration and
/// framework this test assembly was built for. Its connection string travels in DBCHANGE_DACFX162_CONNECTION, never in an argument, since
/// io/Command prints the arguments. Spikes S2, S9, S10 and S11 run their verbs through it.
/// </summary>
internal static class DacFx162Child
{
    /// <summary>The release the child loads, which the first line of its output names as <c>DacFx 162.5.57</c>.</summary>
    public const string Release = "162.5.57";

    /// <summary>The child's dll beside this assembly's build; a missing one fails the test, naming the build that makes it.</summary>
    public static string Dll
    {
        get
        {
            var tests = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            var dll = Path.Combine(Repository.Root, "tests", "DacFx162", "bin", tests.Parent!.Name, tests.Name, "DbChange.DacFx162.dll");
            Assert.True(File.Exists(dll), dll + " does not exist: build tests/DacFx162 with dotnet build DbChange.sln, as CI does.");
            return dll;
        }
    }

    /// <summary>The child run from the repository's root with the verb and its arguments, its connection string given or none.</summary>
    public static Ran.Exited Run(IEnumerable<string> arguments, string? connection = null) =>
        new Command("dotnet", [Dll, .. arguments], Programs.Default)
        {
            Directory = Repository.Root,
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DACFX_TELEMETRY_OPTOUT"] = "1",
                ["DBCHANGE_DACFX162_CONNECTION"] = connection,
            },
        }.Finish();

    /// <summary>The first line of the child's output: the release of the DacFx it loaded.</summary>
    public static string Loaded(Ran.Exited ran) => ran.Output.Split('\n')[0].TrimEnd('\r');

    /// <summary>
    /// The verb's run, which must exit 0 having loaded <see cref="Release"/>; otherwise the test fails with both streams, so a stale build
    /// of the child or a failure of DacFx is named.
    /// </summary>
    public static Ran.Exited Succeeded(IEnumerable<string> arguments, string connection)
    {
        var ran = Run(arguments, connection);
        Assert.True(ran.Code == 0 && Loaded(ran) == "DacFx " + Release,
            "the DacFx 162 child exited " + ran.Code + ", expected 0 and a first line of DacFx " + Release + ":\n" + ran.Output + ran.Errors);
        return ran;
    }
}
