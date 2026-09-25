using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Estate.Budgets.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/Doctor (WP 1.7): read-only checks of the SDK and runtime, the tool folder and its DacFx against the toolchain ledger, the build
/// route, the scratch server and its image, and Git LFS, with a remedy for each item missing, on a machine the test describes; and R13's
/// window, the committed engine against a sample ledger's row (M1 exit 6).
/// </summary>
public sealed class DoctorTests : IDisposable
{
    private const string Version = "3.0.0+0123456789abcdef";

    private readonly string machine = Directory.CreateTempSubdirectory("estate-doctor-").FullName;

    public void Dispose() => Directory.Delete(machine, recursive: true);

    [Fact]
    [Trait("Category", "fast")]
    public void A_bare_machine_gets_a_remedy_for_each_item_missing()
    {
        var checks = Doctor.Examine(machine, null, machine, (_, _) => null, Version);   // no global.json, no tool folder, and nothing installed

        Assert.Equal(["sdk", "runtime", "tool", "dacfx", "build", "scratch-server", "image", "lfs"], checks.Select(c => c.Item));
        Assert.Equal(["sdk", "tool", "build", "scratch-server", "lfs"], checks.Where(c => c.Remedy is not null).Select(c => c.Item));
        Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Found)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_machine_with_every_item_misses_nothing()
    {
        Publish();
        File.WriteAllText(Path.Combine(machine, "global.json"), """{ "sdk": { "version": "10.0.401", "rollForward": "latestPatch" } }""");

        var checks = Doctor.Examine(machine, null, Directory.CreateDirectory(Path.Combine(machine, "estate", "src")).FullName, Answers(new()
        {
            ["dotnet --list-sdks"] = (0, "9.0.314 [x]\n10.0.402 [x]\n"),
            ["docker info"] = (0, "29.5.3\n"),
            ["docker image"] = (0, "sha256:5b0916c7af8c\n"),
            ["git lfs"] = (0, "git-lfs/3.4.0 (GitHub; windows amd64)\n"),
        }), Version);

        Assert.All(checks, c => Assert.Null(c.Remedy));
        Assert.Equal(
            ["sdk=10.0.402", "runtime=" + Environment.Version, "tool=published", "dacfx=" + Doctor.DacFx + " (UNPINNED)", "build=dotnet with the tool folder's targets",
                "scratch-server=docker 29.5.3", "image=present", "lfs=git-lfs/3.4.0"],
            checks.Select(c => c.Item + "=" + c.Found));
    }

    /// <summary>The committed DacFx is the package the build and every plan use: the version Directory.Packages.props pins.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_committed_engine_is_the_DacFx_Directory_Packages_props_pins()
    {
        var pinned = System.Xml.Linq.XDocument.Load(Path.Combine(Repository.Root, "Directory.Packages.props")).Descendants()
            .Single(e => (string?)e.Attribute("Include") == "Microsoft.SqlServer.DacFx").Attribute("Version")!.Value;

        Assert.Equal(pinned, Doctor.DacFx);
        Assert.Equal("170.5.96", Doctor.DacFx);
    }

    /// <summary>
    /// M1 exit 6 (R13): over the sample ledger's row, the committed engine is accepted at the pin and at the release immediately before it,
    /// and anything else, newer or older, is exit 6, as is a ledger with no row for this estate or a malformed one; while the row reads
    /// UNPINNED every engine is accepted and the doctor says UNPINNED; an estate committing no ledger is unpinned.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("the pin", "| 2026-09-25 | 3.0.0 | 170.5.96 | 170.4.71 |", null, "pinned 170.5.96")]
    [InlineData("the release before the pin", "| 2026-09-25 | 3.0.0 | 170.6.10 | 170.5.96 |", null, "pinned 170.6.10")]
    [InlineData("a pin older than the engine", "| 2026-09-25 | 3.0.0 | 170.4.71 | 170.3.93 |", "toolchain.outside-window", "outside the pin 170.4.71")]
    [InlineData("a pin two releases newer", "| 2026-09-25 | 3.0.0 | 170.7.2 | 170.6.10 |", "toolchain.outside-window", "outside the pin 170.7.2")]
    [InlineData("UNPINNED", "| 2026-09-25 | 3.0.0 | UNPINNED | — |", null, "UNPINNED")]
    [InlineData("the latest row of this estate's", "| 2026-09-26 | 3.0.0 | 170.4.71 | 170.3.93 |\n| 2026-09-25 | 3.0.0 | 170.5.96 | — |", "toolchain.outside-window", "outside the pin 170.4.71")]
    [InlineData("no row for this estate", "| 2026-09-25 | 3.1.0 | 170.5.96 | — |", "toolchain.unrecorded", "has no dated row for estate 3.0.0")]
    [InlineData("a malformed pin", "| 2026-09-25 | 3.0.0 | the latest | — |", "toolchain.malformed", "no DacFx release")]
    [InlineData("no ledger", null, null, "UNPINNED")]
    public void The_committed_engine_stands_inside_the_ledger_s_window_only_at_the_pin_or_the_release_before_it(string what, string? rows, string? code, string said)
    {
        if (rows is not null)
        {
            var sample = File.ReadAllText(Path.Combine(Repository.Root, "tests", "Golden", "estate", "ledgers", "toolchain.md"));
            Directory.CreateDirectory(Path.Combine(machine, "estate", "ledgers"));
            File.WriteAllText(Path.Combine(machine, "estate", "ledgers", "toolchain.md"), sample.Replace("| 2026-09-24 | 3.0.0 | UNPINNED | — |", rows, StringComparison.Ordinal));
        }

        var error = Doctor.Toolchain(machine, Version).Match(pin => pin.Rejects(Kernel.Engine.Of(Doctor.DacFx).Match(e => e, r => throw new InvalidOperationException(r.Message))), r => r);
        var dacfx = Doctor.Examine(machine, null, machine, (_, _) => null, Version).Single(c => c.Item == "dacfx");

        Assert.True(code == error?.Code, what + ": " + error?.Code);
        Assert.Equal<int?>(code is null ? null : 6, error is null ? null : Cli.Contract.Exit(error));
        Assert.Equal(code is null, dacfx.Remedy is null);
        Assert.Contains(said, dacfx.Found, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("10.0.415", true)]              // a later patch in the band: rollForward latestPatch
    [InlineData("10.0.400", false)]             // below the pin
    [InlineData("10.0.500", false)]             // the next feature band
    [InlineData("10.0.402-rc.1.25451.1", false)]  // a preview is not the band's release
    [InlineData("9.0.314", false)]
    public void The_sdk_is_found_only_in_the_band_global_json_names(string installed, bool found)
    {
        File.WriteAllText(Path.Combine(machine, "global.json"), """{ "sdk": { "version": "10.0.401" } }""");

        var sdk = Doctor.Examine(machine, null, machine, Answers(new() { ["dotnet --list-sdks"] = (0, installed + " [x]\n") }), Version)[0];

        Assert.Equal(found, sdk.Remedy is null);
        Assert.Contains(found ? installed : "10.0.4xx", sdk.Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Without_Docker_LocalDB_is_the_scratch_server_and_no_image_is_needed()
    {
        var checks = Doctor.Examine(machine, null, machine, Answers(new() { ["docker info"] = (1, "Cannot connect to the Docker daemon"), ["sqllocaldb info"] = (0, "MSSQLLocalDB\n") }), Version).ToDictionary(c => c.Item);

        Assert.Equal(("localdb, CDC not provable here", null), (checks["scratch-server"].Found, checks["scratch-server"].Remedy));
        Assert.Null(checks["image"].Remedy);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Docker_without_the_pinned_image_names_its_pull()
    {
        var image = Doctor.Examine(machine, null, machine, Answers(new() { ["docker info"] = (0, "29.5.3\n"), ["docker image"] = (1, "No such image") }), Version).Single(c => c.Item == "image");

        Assert.Equal("absent", image.Found);
        Assert.Contains("ci/sql.sh up", image.Remedy, StringComparison.Ordinal);
        Assert.Contains(Doctor.SqlServerImage, image.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_sql_scripts_pin_the_image_the_doctor_checks()
    {
        Assert.Matches("^mcr.microsoft.com/mssql/server:2022-latest@sha256:[0-9a-f]{64}$", Doctor.SqlServerImage);
        Assert.All(["sql.sh", "sql.ps1"], script => Assert.Contains(Doctor.SqlServerImage, File.ReadAllText(Path.Combine(Repository.Root, "ci", script)), StringComparison.Ordinal));
    }

    /// <summary>The files a published tool folder holds beside estate: the SqlTasks targets and the reference assemblies.</summary>
    private void Publish()
    {
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(machine, file))!);
            File.WriteAllText(Path.Combine(machine, file), "");
        }
    }

    /// <summary>A machine that answers a program and its first argument as given, and has nothing else installed.</summary>
    private static Doctor.Command Answers(Dictionary<string, (int Exit, string Output)> answers) =>
        (file, arguments) => answers.TryGetValue(file + " " + arguments[0], out var answer) ? answer : null;
}
