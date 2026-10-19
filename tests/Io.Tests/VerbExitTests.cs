using System;
using DbChange.Cli;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// M1 exits 5, 6 and 7 through the verbs (ARCH-14): each refusal those exits name that check drift and read make before anything
/// connects exits with its category's code, the code the answer's first finding. dbchange runs in this process at a repository root the
/// test writes, with no tool folder: no refusal here reaches a build or a server, so nothing is published first.
/// </summary>
public sealed class VerbExitTests : IDisposable
{
    private readonly ScratchFolder root = ScratchFolder.Temporary("verb-exit");

    public void Dispose() => root.Dispose();

    private Checkout Here => new(root.Path, root.Path, null, Contract.Version);

    /// <summary>M1 exit 7's first half: a literal connection string where a target goes is exit 6, and no part of it is printed.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("check drift --target")]
    [InlineData("read --from")]
    [Trait("Value", "X1")]
    [Trait("Value", "X2")]
    [Trait("Exit", "M1.7")]
    public void A_literal_connection_string_as_a_target_is_exit_6_and_printed_nowhere(string verb)
    {
        var asked = verb.Split(' ');
        var (exit, answer, printed) = VerbAnswer.Printing(Here, [.. asked, "Server=db;User ID=sa;Password=" + PlantedValue.PasswordText, .. asked[0] == "check" ? ["--at", "main"] : Array.Empty<string>()]);

        Assert.Equal((6, "connection.literal"), (exit, (string?)answer["findings"]![0]!["code"]));
        PlantedValue.Password.AbsentFrom(printed);
        Assert.DoesNotContain("Server=db", printed, StringComparison.Ordinal);
    }

    /// <summary>M1 exit 6 through the verb: a ledger whose pin the committed DacFx is neither, nor the release before, is exit 6 before anything connects, the stamp naming both.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "R1")]
    [Trait("Exit", "M1.6")]
    public void The_committed_DacFx_outside_the_ledger_s_window_is_exit_6_before_anything_connects()
    {
        root.File("dbchange/ledgers/toolchain.md", "| Date | dbchange | Pinned DacFx | Release before |\n|---|---|---|---|\n| 2026-09-25 | 3.0.0 | " + DoctorTests.Near(2) + " | " + DoctorTests.Near(1) + " |\n");

        var (exit, answer) = VerbAnswer.Of(Here, "check", "drift", "--target", "copy:dbchange_nowhere_1_00000000", "--at", "main");

        Assert.Equal((6, "toolchain.outside-window"), (exit, (string?)answer["findings"]![0]!["code"]));
        Assert.Equal((DoctorTests.PinnedDacFx, DoctorTests.Near(2), null), ((string?)answer["dacfx"], (string?)answer["pin"], answer["server"]));
    }

    /// <summary>
    /// M1 exit 5 through the verb (R15): check drift against a copy the registry does not hold, or against a registered copy whose server
    /// is on the host a named environment names, is exit 9 with the refusal's code.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("dbchange_nowhere_1_00000000", "copy.unregistered")]
    [InlineData("dbchange_host_1_0a1b2c3d", "copy.named-host")]
    [Trait("Law", "a named environment cannot be written")]
    [Trait("Value", "S7")]
    [Trait("Exit", "M1.5")]
    public void A_copy_the_registry_does_not_hold_or_one_on_a_named_environment_s_host_is_exit_9(string copy, string code)
    {
        EnvironmentsJson.Dev("env:DBCHANGE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), host: "localhost").WriteTo(root.Path);
        root.File(".dbchange/copies.json", "{ \"copies\": [ { \"name\": \"dbchange_host_1_0a1b2c3d\", \"server\": \"localhost,11433\", \"machine\": \"host\", \"pid\": 1, \"created\": \"2026-09-24T00:00:00Z\" } ] }");

        var (exit, answer) = VerbAnswer.Of(Here, "check", "drift", "--target", "copy:" + copy, "--at", "main");

        Assert.Equal((9, code), (exit, (string?)answer["findings"]![0]!["code"]));
    }
}
