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
/// io/Doctor (WP 1.7): read-only checks of the SDK and the runtime, git, the tool folder and its DacFx against the toolchain ledger, the
/// build route, the local server dbchange would use, and Git LFS, with a remedy for each item missing, on a machine the test
/// describes and with programs a stand-in runner answers; and R13's window, the committed DacFx against a sample ledger's row.
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
        ["dotnet --list-sdks"] = (0, "9.0.314 [x]\n10.0.402 [x]\n"), ["sqllocaldb info"] = (0, "MSSQLLocalDB\n"),
        ["git --version"] = (0, "git version 2.31.1.windows.1\n"), ["git lfs"] = (0, "git-lfs/3.4.0 (GitHub; windows amd64)\n"),
    };

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "A2")]
    [Trait("Exit", "M0.3")]
    public void A_bare_machine_gets_a_remedy_for_each_item_missing()
    {
        var checks = Doctor.Examine(Bare(), Nothing);   // no global.json, no tool folder, no sql.env, and nothing installed

        Assert.Equal(["sdk", "runtime", "tool", "dacfx", "build", "git", "local-server", "lfs"], checks.Select(c => c.Item.Name));
        Assert.Equal(["sdk", "tool", "build", "git", "local-server", "lfs"],
            checks.Where(c => c.Remedy is not null).Select(c => c.Item.Name));
        Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Found)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_machine_with_every_item_misses_nothing()
    {
        Publish();
        machine.File("global.json", """{ "sdk": { "version": "10.0.401", "rollForward": "latestPatch" } }""");

        var checks = Doctor.Examine(Bare(sqlEnv: SqlEnv(), workingDirectory: machine.Folder(Path.Combine("dbchange", "src"))),
            Answers(Everything));

        Assert.All(checks, c => Assert.Null(c.Remedy));
        Assert.Equal(
            ["sdk=10.0.402", "runtime=" + Environment.Version, "tool=published",
                "dacfx=" + DacFx.Version.Match(v => v.ToString(), e => e.Message) + " (UNPINNED)",
                "build=dotnet with the tool folder's targets", "git=2.31.1",
                // the loopback address as SqlServer.Host spells it
                "local-server=the SQL Server ~/.dbchange/sql.env names (localhost,11434)", "lfs=git-lfs/3.4.0"],
            checks.Select(c => c.Item + "=" + c.Found));
    }

    /// <summary>The DacFx release Directory.Packages.props pins, which the build and every plan use.</summary>
    internal static string PinnedDacFx =>
        System.Xml.Linq.XDocument.Load(Path.Combine(Repository.Root, "Directory.Packages.props")).Descendants()
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
    /// ledger with no row for this dbchange version or a malformed one; while the row reads UNPINNED every DacFx is accepted and the doctor
    /// says UNPINNED; an SSDT repository committing no ledger is unpinned.
    /// </summary>
    public static TheoryData<string, string?, string?, string> Windows => new()
    {
        { "the pin", "| 2026-09-25 | 3.0.0 | " + Committed + " | " + Near(-1) + " |", null, "pinned " + Committed },
        { "the release before the pin", "| 2026-09-25 | 3.0.0 | " + Near(1) + " | " + Committed + " |", null, "pinned " + Near(1) },
        { "a pin older than the DacFx", "| 2026-09-25 | 3.0.0 | " + Near(-1) + " | " + Near(-2) + " |", "toolchain.outside-window",
            "outside the pin " + Near(-1) },
        { "a pin two releases newer", "| 2026-09-25 | 3.0.0 | " + Near(2) + " | " + Near(1) + " |", "toolchain.outside-window",
            "outside the pin " + Near(2) },
        { "UNPINNED", "| 2026-09-25 | 3.0.0 | UNPINNED | — |", null, "UNPINNED" },
        { "the latest row of this dbchange version's",
            "| 2026-09-26 | 3.0.0 | " + Near(-1) + " | " + Near(-2) + " |\n| 2026-09-25 | 3.0.0 | " + Committed + " | — |",
            "toolchain.outside-window", "outside the pin " + Near(-1) },
        { "no row for this dbchange version", "| 2026-09-25 | 3.1.0 | " + Committed + " | — |", "toolchain.unrecorded",
            "has no dated row for dbchange 3.0.0" },
        { "a malformed pin", "| 2026-09-25 | 3.0.0 | the latest | — |", "toolchain.malformed", "no DacFx release" },
        { "no ledger", null, null, "UNPINNED" },
    };

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "R1")]
    [Trait("Value", "R5")]
    [Trait("Exit", "M1.6")]
    [MemberData(nameof(Windows))]
    public void The_committed_DacFx_stands_inside_the_ledger_s_window_only_at_the_pin_or_the_release_before_it(string what, string? rows,
        string? code, string said)
    {
        if (rows is not null)
        {
            Ledger(rows);
        }

        var error = Doctor.Toolchain(machine.Path, Version).Match(pin => pin.Rejects(Value(DacFx.Version)), r => r);
        var dacfx = Doctor.Examine(Bare(), Nothing).Single(c => c.Item == Doctor.Item.DacFx);

        Assert.True(code == error?.Code, what + ": " + error?.Code);
        Assert.Equal(code is null, dacfx.Remedy is null);
        Assert.Contains(said, dacfx.Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_unreadable_toolchain_ledger_is_toolchain_unreadable_and_the_dacfx_item_s_finding()
    {
        var ledger = Ledger("| 2026-09-25 | 3.0.0 | UNPINNED | — |");

        var (error, dacfx) = ErrorPaths.Denied(ledger,
            () => (Failed(Doctor.Toolchain(machine.Path, Version), "toolchain.unreadable"),
                Doctor.Examine(Bare(), Nothing).Single(c => c.Item == Doctor.Item.DacFx)));

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

        var sdk = Doctor.Examine(Bare(), Answers(new() { ["dotnet --list-sdks"] = (0, installed + " [x]\n") }))[0];

        Assert.Equal(found, sdk.Remedy is null);
        Assert.Contains(found ? installed : "10.0.4xx", sdk.Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_malformed_global_json_is_sdk_global_json_naming_the_line_and_the_sdk_item_s_finding()
    {
        machine.File("global.json", "{\n  \"sdk\": { \"version\": 10.0.401 }\n}\n");

        var error = Failed(Doctor.Pinned(machine.Path), "sdk.global-json");
        var sdk = Doctor.Examine(Bare(), Answers(Everything))[0];

        Assert.Contains("is not JSON at line 2", error.Message, StringComparison.Ordinal);
        Assert.Equal((error.Message, error.Remedy), (sdk.Found, sdk.Remedy));
    }

    /// <summary>A program that does not answer in twenty seconds is named as such, never as absent, since the remedy differs: dotnet, git
    /// and sqllocaldb each.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_program_the_doctor_runs_that_does_not_answer_in_time_is_named_as_such_and_not_as_absent()
    {
        var checks = Doctor.Examine(Bare(), (c, _) => new Ran.TimedOut(c.Timeout, "", "")).ToDictionary(c => c.Item.Name);

        Assert.Equal("dotnet did not answer in 20 seconds", checks["sdk"].Found);
        Assert.Equal("git did not answer in 20 seconds", checks["git"].Found);
        Assert.Equal("sqllocaldb did not answer in 20 seconds", checks["local-server"].Found);
        Assert.DoesNotContain("absent", checks["sdk"].Found + checks["git"].Found + checks["local-server"].Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "R5")]
    public void A_runtime_other_than_NET_10_is_refused_with_its_remedy()
    {
        var eleven = Doctor.Examine(Bare(runtime: new Version(11, 0, 0)), Nothing).Single(c => c.Item == Doctor.Item.Runtime);
        var ten = Doctor.Examine(Bare(runtime: new Version(10, 0, 5)), Nothing).Single(c => c.Item == Doctor.Item.Runtime);

        Assert.Equal(("11.0.0", "Install the .NET 10 runtime; dbchange runs on .NET 10 alone."), (eleven.Found, eleven.Remedy));
        Assert.Equal(("10.0.5", null), (ten.Found, ten.Remedy));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Git_older_than_2_24_is_refused_and_git_absent_is_named()
    {
        var old = Doctor.Examine(Bare(), Answers(new() { ["git --version"] = (0, "git version 2.20.1\n") }))
            .Single(c => c.Item == Doctor.Item.Git);
        var absent = Doctor.Examine(Bare(), Nothing).Single(c => c.Item == Doctor.Item.Git);
        var linux = Doctor.Examine(Bare(), Answers(new() { ["git --version"] = (0, "git version 2.43.0\n") }))
            .Single(c => c.Item == Doctor.Item.Git);

        Assert.Contains("older than 2.24", old.Found, StringComparison.Ordinal);
        Assert.Contains("rev-parse --end-of-options", old.Found, StringComparison.Ordinal);
        Assert.NotNull(old.Remedy);
        Assert.Equal(("absent", "Install git and put it on the PATH, then run dbchange doctor."), (absent.Found, absent.Remedy));
        Assert.Equal(("2.43.0", null), (linux.Found, linux.Remedy));
    }

    /// <summary>The tool folder's DacFx build task names the release its targets run: one other than the committed DacFx is a stale
    /// publish, named as a build names it (Ssdt.BuildTargets.Of).</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_tool_folder_whose_DacFx_build_task_is_another_release_is_named_as_a_stale_publish()
    {
        Publish();
        // a file with another version
        File.Copy(Path.Combine(AppContext.BaseDirectory, "DbChange.Kernel.dll"), machine.Under(Ssdt.BuildTargets.Task), overwrite: true);

        var tool = Doctor.Examine(Bare(), Nothing).Single(c => c.Item == Doctor.Item.Tool);

        Assert.Contains("and dbchange runs DacFx " + DacFx.Version.Match(v => v.ToString(), e => e.Message), tool.Found,
            StringComparison.Ordinal);
        Assert.Contains("ci/publish.sh", tool.Remedy, StringComparison.Ordinal);
    }

    /// <summary>DECISIONS.md, 2026-10-19: neither DBCHANGE_SQL nor sql.env names a server, so LocalDB is the local server, found by
    /// sqllocaldb and named as copies record it; the doctor runs no docker, since v3 runs SQL Server in no container.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Where_neither_DBCHANGE_SQL_nor_sql_env_names_a_server_LocalDB_is_the_local_server_and_no_docker_runs()
    {
        Ran NeverDocker(Command c, CancellationToken t) =>
            c.Program == "docker" ? throw new Xunit.Sdk.XunitException(c + " ran") : Answers(Everything)(c, t);

        var checks = Doctor.Examine(Bare(), NeverDocker).ToDictionary(c => c.Item.Name);

        Assert.Equal(("LocalDB MSSQLLocalDB (" + Value(LocalServer.ServerName(null, null, localDb: true)) + ")", null),
            (checks["local-server"].Found, checks["local-server"].Remedy));
        Assert.DoesNotContain("image", checks.Keys);
    }

    /// <summary>DBCHANGE_SQL names the local server first, in io/LocalServer's order: no sqllocaldb runs.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_local_server_is_the_one_dbchange_would_use_DBCHANGE_SQL_first()
    {
        Ran NeverDocker(Command c, CancellationToken t) =>
            c.Program is "docker" or "sqllocaldb" ? throw new Xunit.Sdk.XunitException(c + " ran while DBCHANGE_SQL names the server")
                : Answers(Everything)(c, t);

        var checks = Doctor.Examine(
            Bare(dbChangeSql: "Server=tcp:DB-Host,1433;User ID=sa;Password=" + PlantedValue.Password, sqlEnv: SqlEnv()), NeverDocker)
            .ToDictionary(c => c.Item.Name);

        Assert.Equal(("DBCHANGE_SQL (db-host,1433)", null), (checks["local-server"].Found, checks["local-server"].Remedy));
        PlantedValue.Password.AbsentFrom(string.Join(" ", checks.Values.Select(c => c.Found + c.Remedy)));
    }

    /// <summary>With no local server, the remedy names the scripts that start one, ci/sql.sh up and ci/sql.ps1 up, and DBCHANGE_SQL;
    /// io itself starts none (DECISIONS.md, 2026-10-19).</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void With_no_local_server_the_remedy_names_ci_sql_up_and_DBCHANGE_SQL()
    {
        var none = Doctor.Examine(Bare(), Nothing).Single(c => c.Item == Doctor.Item.LocalServer);

        Assert.StartsWith("none: DBCHANGE_SQL is unset, ", none.Found, StringComparison.Ordinal);
        Assert.Contains("ci/sql.sh up", none.Remedy, StringComparison.Ordinal);
        Assert.Contains("ci/sql.ps1 up", none.Remedy, StringComparison.Ordinal);
        Assert.Contains("DBCHANGE_SQL", none.Remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("Docker", none.Found + none.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// A resumed session, or one after ci/sql.sh down: sql.env still names the server, sqlservr is stopped, and nothing accepts a
    /// connection at its port. The item is missing with the remedy to start it, so the line reads DEGRADED instead of READY; a server
    /// DBCHANGE_SQL names gets the remedy to start or correct that one. The doctor asks for the port alone and logs in to nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_local_server_accepting_no_connection_at_its_port_is_missing_with_the_remedy_to_start_it()
    {
        var asked = new List<(string, int)>();
        bool Closed(string host, int port, CancellationToken _)
        {
            asked.Add((host, port));
            return false;
        }

        var stopped = Doctor.Examine(Bare(sqlEnv: SqlEnv(), listens: Closed), Answers(Everything))
            .Single(c => c.Item == Doctor.Item.LocalServer);
        var variable = Doctor.Examine(Bare(dbChangeSql: "Server=tcp:DB-Host,1433;Integrated Security=true", listens: Closed),
            Answers(Everything)).Single(c => c.Item == Doctor.Item.LocalServer);

        Assert.Equal("the SQL Server ~/.dbchange/sql.env names (localhost,11434), accepting no connection on 127.0.0.1,11434 within "
            + Command.Written(Command.ProbeTimeout), stopped.Found);
        Assert.Equal("Run ci/sql.sh up, or ci/sql.ps1 up on Windows, then run dbchange doctor.", stopped.Remedy);
        Assert.Contains("DBCHANGE_SQL", variable.Remedy, StringComparison.Ordinal);
        Assert.Equal([("127.0.0.1", 11434), ("DB-Host", 1433)], asked);
        Assert.False(new Doctor.Readiness([stopped]).Ready);
    }

    /// <summary>
    /// The doctor reaches a server at the TCP port its data source names, as SqlClient reads it (1433 for a host alone, and localhost,1433
    /// for an empty data source, as LocalServer.ServerName spells it), and does not reach one over another transport or a named instance
    /// without a port, whose port SQL Server Browser gives.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(@"Server=tcp:DB-Host,1433", "DB-Host", 1433)]
    [InlineData(@"Server=127.0.0.1,11434", "127.0.0.1", 11434)]
    [InlineData(@"Server=db-host", "db-host", 1433)]
    [InlineData(@"Server=.,1433", "localhost", 1433)]
    [InlineData(@"Server=", "localhost", 1433)]
    [InlineData(@"Server=db-host\sql2022,51000", "db-host", 51000)]
    [InlineData(@"Server=db-host\sql2022", null, 0)]
    [InlineData(@"Server=np:\\db-host\pipe\sql\query", null, 0)]
    [InlineData(@"Server=lpc:db-host", null, 0)]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB", null, 0)]
    public void The_doctor_reaches_the_TCP_port_a_data_source_names_and_no_other_transport(string connection, string? host, int port) =>
        Assert.Equal(host is null ? null : (host, port), Doctor.Endpoint(connection + ";Integrated Security=true"));

    /// <summary>
    /// Doctor.Listening on this machine: a loopback port nothing listens on accepts no connection, and one a listener holds does, each
    /// answered without waiting for the probe timeout.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_closed_loopback_port_accepts_no_connection_and_a_listening_one_does()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var open = Doctor.Listening("127.0.0.1", port, CancellationToken.None);
        var opened = clock.Elapsed;
        listener.Stop();
        clock.Restart();
        var closed = Doctor.Listening("127.0.0.1", port, CancellationToken.None);

        Assert.Equal((true, false), (open, closed));
        Assert.All((TimeSpan[])[opened, clock.Elapsed], took => Assert.True(took < TimeSpan.FromSeconds(5),
            took + " for a loopback answer, against a probe timeout of " + Command.ProbeTimeout));
    }

    /// <summary>
    /// S15 of the pre-M2 review: the local server is chosen once, by LocalServer.Choice, which the doctor and a copy's resolution both ask,
    /// and LocalDB is asked about (sqllocaldb) only when neither DBCHANGE_SQL nor sql.env gives a server; every copy's resolution once ran
    /// sqllocaldb info first, bounded at twenty seconds.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void LocalDB_is_asked_about_only_when_neither_DBCHANGE_SQL_nor_sql_env_gives_a_server()
    {
        var asked = 0;
        bool Installed()
        {
            asked++;
            return true;
        }

        var named = Value(LocalServer.Choice(null, SqlEnv(), Installed));
        var variable = Value(LocalServer.Choice("Server=db", null, Installed));
        var beforeLocalDb = asked;
        var localDb = Value(LocalServer.Choice(null, machine.Under("no-sql.env"), Installed));

        Assert.Equal(("the SQL Server ~/.dbchange/sql.env names", "DBCHANGE_SQL", "LocalDB MSSQLLocalDB"),
            (named.ToString(), variable.ToString(), localDb.ToString()));
        Assert.Equal((0, 1), (beforeLocalDb, asked));
    }

    /// <summary>
    /// DECISIONS.md, 2026-10-19: ci/sql.sh installs SQL Server 2022 (16.x) Express natively and runs sqlservr as a process, recording its
    /// port in ~/.dbchange/sql.env (11434 unless DBCHANGE_SQL_PORT says otherwise); neither script pulls an image or runs docker, and
    /// ci/sql.ps1 starts LocalDB.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_sql_scripts_start_SQL_Server_2022_Express_on_Linux_and_LocalDB_on_Windows_and_run_no_container()
    {
        var (sh, ps1) = (File.ReadAllText(Path.Combine(Repository.Root, "ci", "sql.sh")),
            File.ReadAllText(Path.Combine(Repository.Root, "ci", "sql.ps1")));

        Assert.Matches(@"(?m)^version='16\.0\.\d+\.\d+-\d+'$", sh);
        Assert.Contains("MSSQL_PID=Express", sh, StringComparison.Ordinal);
        Assert.Contains("${DBCHANGE_SQL_PORT:-11434}", sh, StringComparison.Ordinal);
        Assert.Contains("sqllocaldb", ps1, StringComparison.Ordinal);
        Assert.All(new[] { sh, ps1 }, script => Assert.DoesNotMatch(@"(?i)\bdocker\b|mcr\.microsoft\.com", script));
    }

    /// <summary>A DacFx release near the committed one: its second group moved by <paramref name="minors"/>, so the rows above hold
    /// whatever release the build pins.</summary>
    internal static string Near(int minors)
    {
        var release = System.Version.Parse(Committed);
        return string.Create(CultureInfo.InvariantCulture, $"{release.Major}.{release.Minor + minors}.{release.Build}");
    }

    /// <summary>A machine holding nothing but the test folder, the repository root: no DBCHANGE_SQL, no sql.env unless given, this
    /// process's runtime unless given, and a server accepting a connection at every host and port unless given.</summary>
    private Doctor.Machine Bare(string? dbChangeSql = null, string? sqlEnv = null, Version? runtime = null,
        string? workingDirectory = null, Func<string, int, CancellationToken, bool>? listens = null) =>
        new(new Checkout(machine.Path, workingDirectory ?? machine.Path, null, Version), machine.Path, dbChangeSql,
            sqlEnv ?? machine.Under("no-sql.env"), runtime ?? Environment.Version, listens ?? ((_, _, _) => true));

    /// <summary>A sql.env as ci/sql.sh writes it, naming the local server's port and password.</summary>
    private string SqlEnv() => machine.File("sql.env", "MSSQL_SA_PASSWORD=" + PlantedValue.Password + "\nDBCHANGE_SQL_PORT=11434\n");

    /// <summary>The sample toolchain ledger under the machine's repository root, its one row replaced; the file's path.</summary>
    private string Ledger(string rows)
    {
        var sample = File.ReadAllText(Path.Combine(Repository.Root, "tests", "Golden", "dbchange", "ledgers", "toolchain.md"));
        return machine.File(Path.Combine("dbchange", "ledgers", "toolchain.md"),
            sample.Replace("| 2026-09-24 | 3.0.0 | UNPINNED | — |", rows, StringComparison.Ordinal));
    }

    /// <summary>The files a published tool folder holds beside dbchange: the SqlTasks targets and the reference assemblies.</summary>
    private void Publish()
    {
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll",
            "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            machine.File(file, "");
        }

        // the committed DacFx's build task, as ci/publish copies it
        File.Copy(Path.Combine(AppContext.BaseDirectory, Ssdt.BuildTargets.Task), machine.Under(Ssdt.BuildTargets.Task));
    }

    /// <summary>
    /// S14 of the pre-M2 review: the tool item makes the check a build makes, so a tool folder whose DacFx build task carries no file
    /// version is named with the remedy to publish again, as a build refuses it; the doctor read the version with a reader of its own,
    /// which passed a task that carries none.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_tool_folder_whose_build_task_carries_no_file_version_is_named_with_the_remedy_to_publish_again()
    {
        Publish();
        machine.File(Ssdt.BuildTargets.Task, "");

        var tool = Doctor.Examine(Bare(), Nothing).Single(c => c.Item == Doctor.Item.Tool);

        Assert.Contains("carries no file version", tool.Found, StringComparison.Ordinal);
        Assert.Contains("ci/publish.sh", tool.Remedy, StringComparison.Ordinal);
    }

    /// <summary>A machine on which no program is installed.</summary>
    private static Ran Nothing(Command command, CancellationToken cancel) =>
        new Ran.NotFound(command.Program, "'" + command.Program + "' is on no folder of the PATH.");

    /// <summary>A machine that answers a program and its first argument as given, and has nothing else installed.</summary>
    internal static Runner Answers(Dictionary<string, (int Exit, string Output)> answers) =>
        (command, _) => answers.TryGetValue(command.Program + " " + command.Arguments[0], out var answer)
            ? new Ran.Exited(answer.Exit, answer.Output, "") : Nothing(command, default);
}
