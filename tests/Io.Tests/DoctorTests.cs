using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DbChange.Budgets.Tests;
using DbChange.Budgets.Tests.Register;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// io/Doctor (WP 1.7): read-only checks of the SDK and the runtime, git, the tool folder and its DacFx against the toolchain ledger, the build
/// route, the local server dbchange would use and its image, and Git LFS, with a remedy for each item missing, on a machine the test describes
/// and with programs a stand-in runner answers; and R13's window, the committed DacFx against a sample ledger's row.
/// </summary>
public sealed class DoctorTests : IDisposable
{
    /// <summary>The DacFx release this build carries, as io/DacFx reads it once.</summary>
    private static string Committed => Value(DacFx.Version).ToString();

    private const string Version = "3.0.0+0123456789abcdef";

    private readonly ScratchFolder machine = ScratchFolder.Temporary("doctor");

    public void Dispose() => machine.Dispose();

    /// <summary>The programs a machine with every item present answers.</summary>
    private static Dictionary<string, (int Exit, string Output)> Everything => new()
    {
        ["dotnet --list-sdks"] = (0, "9.0.314 [x]\n10.0.402 [x]\n"), ["docker info"] = (0, "29.5.3\n"), ["docker image"] = (0, "sha256:5b0916c7af8c\n"),
        ["docker container"] = (0, Doctor.SqlServerImage + "\n"), ["git --version"] = (0, "git version 2.31.1.windows.1\n"), ["git lfs"] = (0, "git-lfs/3.4.0 (GitHub; windows amd64)\n"),
    };

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "A2")]
    [Trait("Exit", "M0.3")]
    public void A_bare_machine_gets_a_remedy_for_each_item_missing()
    {
        var checks = Doctor.Examine(Bare(), Nothing, Version);   // no global.json, no tool folder, no sql.env, and nothing installed

        Assert.Equal(["sdk", "runtime", "tool", "dacfx", "build", "git", "local-server", "image", "lfs"], checks.Select(c => c.Item.Name));
        Assert.Equal(["sdk", "tool", "build", "git", "local-server", "lfs"], checks.Where(c => c.Remedy is not null).Select(c => c.Item.Name));
        Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Found)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_machine_with_every_item_misses_nothing()
    {
        Publish();
        machine.File("global.json", """{ "sdk": { "version": "10.0.401", "rollForward": "latestPatch" } }""");

        var checks = Doctor.Examine(Bare(sqlEnv: SqlEnv()) with { WorkingDirectory = machine.Folder(Path.Combine("dbchange", "src")) }, Answers(Everything), Version);

        Assert.All(checks, c => Assert.Null(c.Remedy));
        Assert.Equal(
            ["sdk=10.0.402", "runtime=" + Environment.Version, "tool=published", "dacfx=" + DacFx.Version.Match(v => v.ToString(), e => e.Message) + " (UNPINNED)", "build=dotnet with the tool folder's targets", "git=2.31.1",
                "local-server=dbchange-sql container (localhost,11433)", "image=present", "lfs=git-lfs/3.4.0"],   // the loopback address as SqlServer.Host spells it
            checks.Select(c => c.Item + "=" + c.Found));
    }

    /// <summary>The DacFx release Directory.Packages.props pins, which the build and every plan use.</summary>
    internal static string PinnedDacFx => System.Xml.Linq.XDocument.Load(Path.Combine(Repository.Root, "Directory.Packages.props")).Descendants()
        .Single(e => (string?)e.Attribute("Include") == "Microsoft.SqlServer.DacFx").Attribute("Version")!.Value;

    /// <summary>The committed DacFx is the package the build and every plan use: the version Directory.Packages.props pins.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "R1")]
    public void The_committed_DacFx_is_the_release_Directory_Packages_props_pins() =>
        Assert.Equal(PinnedDacFx, DacFx.Version.Match(v => v.ToString(), e => e.Message));

    /// <summary>
    /// R13's window over the sample ledger's row, each row written relative to the committed DacFx (<see cref="Near"/>): the committed
    /// DacFx is accepted at the pin and at the release immediately before it, and anything else, newer or older, is rejected, as is a
    /// ledger with no row for this dbchange version or a malformed one; while the row reads UNPINNED every DacFx is accepted and the doctor says
    /// UNPINNED; an SSDT repository committing no ledger is unpinned.
    /// </summary>
    public static TheoryData<string, string?, string?, string> Windows => new()
    {
        { "the pin", "| 2026-09-25 | 3.0.0 | " + Committed + " | " + Near(-1) + " |", null, "pinned " + Committed },
        { "the release before the pin", "| 2026-09-25 | 3.0.0 | " + Near(1) + " | " + Committed + " |", null, "pinned " + Near(1) },
        { "a pin older than the DacFx", "| 2026-09-25 | 3.0.0 | " + Near(-1) + " | " + Near(-2) + " |", "toolchain.outside-window", "outside the pin " + Near(-1) },
        { "a pin two releases newer", "| 2026-09-25 | 3.0.0 | " + Near(2) + " | " + Near(1) + " |", "toolchain.outside-window", "outside the pin " + Near(2) },
        { "UNPINNED", "| 2026-09-25 | 3.0.0 | UNPINNED | — |", null, "UNPINNED" },
        { "the latest row of this dbchange version's", "| 2026-09-26 | 3.0.0 | " + Near(-1) + " | " + Near(-2) + " |\n| 2026-09-25 | 3.0.0 | " + Committed + " | — |", "toolchain.outside-window", "outside the pin " + Near(-1) },
        { "no row for this dbchange version", "| 2026-09-25 | 3.1.0 | " + Committed + " | — |", "toolchain.unrecorded", "has no dated row for dbchange 3.0.0" },
        { "a malformed pin", "| 2026-09-25 | 3.0.0 | the latest | — |", "toolchain.malformed", "no DacFx release" },
        { "no ledger", null, null, "UNPINNED" },
    };

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "R1")]
    [Trait("Value", "R5")]
    [Trait("Exit", "M1.6")]
    [MemberData(nameof(Windows))]
    public void The_committed_DacFx_stands_inside_the_ledger_s_window_only_at_the_pin_or_the_release_before_it(string what, string? rows, string? code, string said)
    {
        if (rows is not null)
        {
            Ledger(rows);
        }

        var error = Doctor.Toolchain(machine.Path, Version).Match(pin => pin.Rejects(Value(DacFx.Version)), r => r);
        var dacfx = Doctor.Examine(Bare(), Nothing, Version).Single(c => c.Item == Doctor.Item.DacFx);

        Assert.True(code == error?.Code, what + ": " + error?.Code);
        Assert.Equal(code is null, dacfx.Remedy is null);
        Assert.Contains(said, dacfx.Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_unreadable_toolchain_ledger_is_toolchain_unreadable_and_the_dacfx_item_s_finding()
    {
        var ledger = Ledger("| 2026-09-25 | 3.0.0 | UNPINNED | — |");

        var (error, dacfx) = ErrorPaths.Denied(ledger, () => (Failed(Doctor.Toolchain(machine.Path, Version), "toolchain.unreadable"), Doctor.Examine(Bare(), Nothing, Version).Single(c => c.Item == Doctor.Item.DacFx)));

        Assert.Contains("cannot be read", error.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be read", dacfx.Found, StringComparison.Ordinal);
        Assert.Contains("read access", dacfx.Remedy, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "R5")]
    [InlineData("10.0.415", true)]              // a later patch in the band: rollForward latestPatch
    [InlineData("10.0.400", false)]             // below the pin
    [InlineData("10.0.500", false)]             // the next feature band
    [InlineData("10.0.402-rc.1.25451.1", false)]  // a preview is not the band's release
    [InlineData("9.0.314", false)]
    public void The_sdk_is_found_only_in_the_band_global_json_names(string installed, bool found)
    {
        machine.File("global.json", """{ "sdk": { "version": "10.0.401" } }""");

        var sdk = Doctor.Examine(Bare(), Answers(new() { ["dotnet --list-sdks"] = (0, installed + " [x]\n") }), Version)[0];

        Assert.Equal(found, sdk.Remedy is null);
        Assert.Contains(found ? installed : "10.0.4xx", sdk.Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_malformed_global_json_is_sdk_global_json_naming_the_line_and_the_sdk_item_s_finding()
    {
        machine.File("global.json", "{\n  \"sdk\": { \"version\": 10.0.401 }\n}\n");

        var error = Failed(Doctor.Pinned(machine.Path), "sdk.global-json");
        var sdk = Doctor.Examine(Bare(), Answers(Everything), Version)[0];

        Assert.Contains("is not JSON at line 2", error.Message, StringComparison.Ordinal);
        Assert.Equal((error.Message, error.Remedy), (sdk.Found, sdk.Remedy));
    }

    /// <summary>A program that does not answer in twenty seconds is named as such, never as absent, since the remedy differs: dotnet, git and docker each.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_program_the_doctor_runs_that_does_not_answer_in_time_is_named_as_such_and_not_as_absent()
    {
        var checks = Doctor.Examine(Bare(), (c, _) => new Ran.TimedOut(c.Timeout, "", ""), Version).ToDictionary(c => c.Item.Name);

        Assert.Equal("dotnet did not answer in 20 seconds", checks["sdk"].Found);
        Assert.Equal("git did not answer in 20 seconds", checks["git"].Found);
        Assert.Contains("restart Docker", checks["local-server"].Remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("absent", checks["sdk"].Found + checks["git"].Found + checks["local-server"].Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "R5")]
    public void A_runtime_other_than_NET_10_is_refused_with_its_remedy()
    {
        var eleven = Doctor.Examine(Bare(runtime: new Version(11, 0, 0)), Nothing, Version).Single(c => c.Item == Doctor.Item.Runtime);
        var ten = Doctor.Examine(Bare(runtime: new Version(10, 0, 5)), Nothing, Version).Single(c => c.Item == Doctor.Item.Runtime);

        Assert.Equal(("11.0.0", "Install the .NET 10 runtime; dbchange runs on .NET 10 alone."), (eleven.Found, eleven.Remedy));
        Assert.Equal(("10.0.5", null), (ten.Found, ten.Remedy));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Git_older_than_2_24_is_refused_and_git_absent_is_named()
    {
        var old = Doctor.Examine(Bare(), Answers(new() { ["git --version"] = (0, "git version 2.20.1\n") }), Version).Single(c => c.Item == Doctor.Item.Git);
        var absent = Doctor.Examine(Bare(), Nothing, Version).Single(c => c.Item == Doctor.Item.Git);
        var linux = Doctor.Examine(Bare(), Answers(new() { ["git --version"] = (0, "git version 2.43.0\n") }), Version).Single(c => c.Item == Doctor.Item.Git);

        Assert.Contains("older than 2.24", old.Found, StringComparison.Ordinal);
        Assert.Contains("rev-parse --end-of-options", old.Found, StringComparison.Ordinal);
        Assert.NotNull(old.Remedy);
        Assert.Equal(("absent", "Install git and put it on the PATH, then run dbchange doctor."), (absent.Found, absent.Remedy));
        Assert.Equal(("2.43.0", null), (linux.Found, linux.Remedy));
    }

    /// <summary>The tool folder's DacFx build task names the release its targets run: one other than the committed DacFx is a stale publish.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_tool_folder_whose_DacFx_build_task_is_another_release_is_named_as_a_stale_publish()
    {
        Publish();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "DbChange.Kernel.dll"), machine.Under("Microsoft.Data.Tools.Schema.Tasks.Sql.dll"));   // a file with another version

        var tool = Doctor.Examine(Bare(), Nothing, Version).Single(c => c.Item == Doctor.Item.Tool);

        Assert.Contains("while dbchange runs DacFx " + DacFx.Version.Match(v => v.ToString(), e => e.Message), tool.Found, StringComparison.Ordinal);
        Assert.Contains("ci/publish.sh", tool.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Without_Docker_LocalDB_is_the_local_server_and_no_image_is_needed()
    {
        var checks = Doctor.Examine(Bare(), Answers(new() { ["docker info"] = (1, "Cannot connect to the Docker daemon"), ["sqllocaldb info"] = (0, "MSSQLLocalDB\n") }), Version).ToDictionary(c => c.Item.Name);

        Assert.Equal(("LocalDB MSSQLLocalDB, CDC not provable here", null), (checks["local-server"].Found, checks["local-server"].Remedy));
        Assert.Equal(("not needed without Docker", null), (checks["image"].Found, checks["image"].Remedy));
    }

    /// <summary>DBCHANGE_SQL names the local server first, in io/LocalServer's order: no docker or sqllocaldb runs, and the image is not needed.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_local_server_is_the_one_dbchange_would_use_DBCHANGE_SQL_first()
    {
        Ran NeverDocker(Command c, CancellationToken t) => c.Program is "docker" or "sqllocaldb" ? throw new Xunit.Sdk.XunitException(c + " ran while DBCHANGE_SQL names the server") : Answers(Everything)(c, t);

        var checks = Doctor.Examine(Bare(dbChangeSql: "Server=tcp:DB-Host,1433;User ID=sa;Password=" + PlantedValue.Password, sqlEnv: SqlEnv()), NeverDocker, Version).ToDictionary(c => c.Item.Name);

        Assert.Equal(("DBCHANGE_SQL (db-host,1433)", null), (checks["local-server"].Found, checks["local-server"].Remedy));
        Assert.Equal(("not needed: DBCHANGE_SQL names the server", null), (checks["image"].Found, checks["image"].Remedy));
        PlantedValue.Password.AbsentFrom(string.Join(" ", checks.Values.Select(c => c.Found + c.Remedy)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Docker_installed_with_its_daemon_stopped_says_to_start_Docker_and_Docker_absent_says_to_install_it()
    {
        var stopped = Doctor.Examine(Bare(), Answers(new() { ["docker info"] = (1, "") }), Version).Single(c => c.Item == Doctor.Item.LocalServer);
        var absent = Doctor.Examine(Bare(), Nothing, Version).Single(c => c.Item == Doctor.Item.LocalServer);
        var noContainer = Doctor.Examine(Bare(), Answers(Everything), Version).Single(c => c.Item == Doctor.Item.LocalServer);
        var daemonDown = Doctor.Examine(Bare(sqlEnv: SqlEnv()), Answers(new() { ["docker info"] = (1, "") }), Version).Single(c => c.Item == Doctor.Item.LocalServer);

        Assert.Contains("start Docker Desktop", stopped.Remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("Install Docker", stopped.Remedy, StringComparison.Ordinal);
        Assert.Contains("Install Docker", absent.Remedy, StringComparison.Ordinal);
        Assert.Contains("ci/sql.sh up", noContainer.Remedy, StringComparison.Ordinal);
        Assert.Equal("dbchange-sql container (localhost,11433)", daemonDown.Found);
        Assert.Contains("start Docker Desktop", daemonDown.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Docker_without_the_pinned_image_names_its_pull_and_a_container_running_another_image_says_to_recreate_it()
    {
        var absent = Doctor.Examine(Bare(), Answers(new() { ["docker info"] = (0, "29.5.3\n"), ["docker image"] = (1, "No such image") }), Version).Single(c => c.Item == Doctor.Item.Image);
        var other = Doctor.Examine(Bare(), Answers(new(Everything) { ["docker container"] = (0, "mcr.microsoft.com/mssql/server:2019-latest\n") }), Version).Single(c => c.Item == Doctor.Item.Image);

        Assert.Equal("absent", absent.Found);
        Assert.Contains("ci/sql.sh up", absent.Remedy, StringComparison.Ordinal);
        Assert.Contains(Doctor.SqlServerImage, absent.Remedy, StringComparison.Ordinal);
        Assert.Equal("present, and dbchange-sql runs mcr.microsoft.com/mssql/server:2019-latest", other.Found);
        Assert.Contains("ci/sql.sh down", other.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_sql_scripts_pin_the_image_the_doctor_checks()
    {
        Assert.Matches("^mcr.microsoft.com/mssql/server:2022-latest@sha256:[0-9a-f]{64}$", Doctor.SqlServerImage);
        Assert.All(["sql.sh", "sql.ps1"], script => Assert.Contains(Doctor.SqlServerImage, File.ReadAllText(Path.Combine(Repository.Root, "ci", script)), StringComparison.Ordinal));
    }

    /// <summary>A DacFx release near the committed one: its second group moved by <paramref name="minors"/>, so the rows above hold whatever release the build pins.</summary>
    private static string Near(int minors)
    {
        var release = System.Version.Parse(Committed);
        return string.Create(CultureInfo.InvariantCulture, $"{release.Major}.{release.Minor + minors}.{release.Build}");
    }

    /// <summary>A machine holding nothing but the test folder: no DBCHANGE_SQL, no sql.env unless given, this process's runtime unless given.</summary>
    private Doctor.Machine Bare(string? dbChangeSql = null, string? sqlEnv = null, Version? runtime = null) =>
        new(machine.Path, null, machine.Path, dbChangeSql, sqlEnv ?? machine.Under("no-sql.env"), runtime ?? Environment.Version);

    /// <summary>A sql.env as ci/sql.sh writes it, naming the container's port and password.</summary>
    private string SqlEnv() => machine.File("sql.env", "MSSQL_SA_PASSWORD=" + PlantedValue.Password + "\nDBCHANGE_SQL_PORT=11433\n");

    /// <summary>The sample toolchain ledger under the machine's repository root, its one row replaced; the file's path.</summary>
    private string Ledger(string rows)
    {
        var sample = File.ReadAllText(Path.Combine(Repository.Root, "tests", "Golden", "dbchange", "ledgers", "toolchain.md"));
        return machine.File(Path.Combine("dbchange", "ledgers", "toolchain.md"), sample.Replace("| 2026-09-24 | 3.0.0 | UNPINNED | — |", rows, StringComparison.Ordinal));
    }

    /// <summary>The files a published tool folder holds beside dbchange: the SqlTasks targets and the reference assemblies.</summary>
    private void Publish()
    {
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            machine.File(file, "");
        }
    }

    /// <summary>A machine on which no program is installed.</summary>
    private static Ran Nothing(Command command, CancellationToken cancel) => new Ran.NotFound(command.Program, "'" + command.Program + "' is on no folder of the PATH.");

    /// <summary>A machine that answers a program and its first argument as given, and has nothing else installed.</summary>
    internal static Runner Answers(Dictionary<string, (int Exit, string Output)> answers) =>
        (command, _) => answers.TryGetValue(command.Program + " " + command.Arguments[0], out var answer) ? new Ran.Exited(answer.Exit, answer.Output, "") : Nothing(command, default);
}
