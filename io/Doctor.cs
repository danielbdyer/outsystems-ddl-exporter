using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using DbChange.Kernel;
using Microsoft.SqlServer.Dac;

namespace DbChange.Io;

/// <summary>
/// Can this machine do the work, read-only (V3_ARCHITECTURE.md §8.12): the .NET SDK in the band global.json names and the .NET 10 runtime;
/// git at 2.24 or later (rev-parse --end-of-options); the committed tool folder and its DacFx against the SSDT repository's toolchain ledger; the
/// build route; the local server dbchange would use, in LocalServer's order (DBCHANGE_SQL, the dbchange-sql container, LocalDB), with the
/// Docker-specific cause when none applies; the pinned SQL Server image and the container's; and Git LFS. Each item missing carries its
/// remedy. Every program runs through io/Command for at most <see cref="ProgramTimeout"/>, and a program that does not answer in time is
/// named as such, never as absent. The machine is read once (<see cref="Machine.Here"/>) and given to <see cref="Examine"/>, so a test
/// describes a machine instead of setting variables.
/// </summary>
public static class Doctor
{
    /// <summary>The local server's image, pinned by tag and digest (§1 fact 12); ci/sql.sh and ci/sql.ps1 run the same one.</summary>
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    /// <summary>The container ci/sql.sh and ci/sql.ps1 run the pinned image as.</summary>
    public const string Container = "dbchange-sql";

    /// <summary>The toolchain ledger, from the repository root: one dated row per dbchange version, with the pinned DacFx or UNPINNED.</summary>
    public const string Ledger = "dbchange/ledgers/toolchain.md";

    /// <summary>How long each program the doctor runs may take: docker info waits while Docker Desktop starts, and dotnet --list-sdks answers in a second.</summary>
    public static readonly TimeSpan ProgramTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The runtime dbchange runs on alone (VALUES.md R5).</summary>
    private const int RuntimeMajor = 10;

    /// <summary>The git release that first takes rev-parse --end-of-options, which io/Git passes so a ref is never read as an option.</summary>
    private static readonly Version GitLeast = new(2, 24);

    /// <summary>The files a published tool folder holds beside dbchange: DacFx's SqlTasks targets and the reference assemblies (mscorlib.dll and FrameworkList.xml).</summary>
    private static readonly string[] Published = ["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"];

    /// <summary>The DacFx build task a published tool folder carries, whose file version names the DacFx release the folder's targets run.</summary>
    private const string BuildTask = "Microsoft.Data.Tools.Schema.Tasks.Sql.dll";

    /// <summary>A ledger row: | date | dbchange version | pinned DacFx or UNPINNED | the release before the pin, or — |.</summary>
    private static readonly Regex Row = new(@"^\|\s*(\d{4}-\d{2}-\d{2})\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|\s*$", RegexOptions.CultureInvariant);

    /// <summary>What this machine holds, read once: where dbchange runs (the checkout), the folder dbchange runs from, DBCHANGE_SQL, the path of ~/.dbchange/sql.env (null where the user's profile folder is unknown), and the runtime.</summary>
    public sealed record Machine(Checkout Checkout, string Running, string? DbChangeSql, string? SqlEnv, Version Runtime)
    {
        /// <summary>The machine dbchange runs on, around the checkout cli already read.</summary>
        public static Machine Here(Checkout checkout) =>
            new(checkout, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("DBCHANGE_SQL"), LocalState.UserSqlEnv, Environment.Version);
    }

    /// <summary>An item the doctor examines, one of a closed set, written as the envelope names it (sdk, local-server).</summary>
    public sealed class Item
    {
        public static readonly Item Sdk = new("sdk");
        public static readonly Item Runtime = new("runtime");
        public static readonly Item Tool = new("tool");
        public static readonly Item DacFx = new("dacfx");
        public static readonly Item Build = new("build");
        public static readonly Item Git = new("git");
        public static readonly Item LocalServer = new("local-server");
        public static readonly Item Image = new("image");
        public static readonly Item Lfs = new("lfs");

        private Item(string name) => Name = name;

        public string Name { get; }

        public override string ToString() => Name;
    }

    /// <summary>What the examination of one item found, and its remedy when the item is missing.</summary>
    public sealed record Prerequisite(Item Item, string Found, string? Remedy);

    /// <summary>The pinned image's digest, which the image item compares the dbchange-sql container's image with.</summary>
    public static string ImageDigest => SqlServerImage[(SqlServerImage.IndexOf('@', StringComparison.Ordinal) + 1)..];

    public static IReadOnlyList<Prerequisite> Examine(Machine machine, Runner run, CancellationToken cancel = default)
    {
        var docker = machine.DbChangeSql is { Length: > 0 } ? null : run(Program("docker", "info", "--format", "{{.ServerVersion}}"), cancel);
        var (sdk, tool) = (Sdk(machine.Checkout.WorkingDirectory, run, cancel), Tool(machine));
        var lfs = run(Program("git", "lfs", "version"), cancel) is Ran.Exited { Code: 0 } said ? said.Output.Trim().Split(' ')[0] : null;
        return
        [
            sdk,
            machine.Runtime.Major == RuntimeMajor ? new(Item.Runtime, machine.Runtime.ToString(), null)
                : new(Item.Runtime, machine.Runtime.ToString(), "Install the .NET " + RuntimeMajor.ToString(CultureInfo.InvariantCulture) + " runtime; dbchange runs on .NET " + RuntimeMajor.ToString(CultureInfo.InvariantCulture) + " alone."),
            tool,
            Committed(machine.Checkout.Root, machine.Checkout.Version),
            sdk.Remedy is null && tool.Remedy is null ? new(Item.Build, "dotnet with the tool folder's targets", null) : new(Item.Build, "none", "Install what the sdk and tool items name, then run dbchange doctor."),
            GitVersion(run, cancel),
            LocalServerChoice(machine, docker, run, cancel),
            Image(machine, docker, run, cancel),
            lfs is null ? new(Item.Lfs, "absent", "Install Git LFS and run git lfs install; the SSDT repository keeps its tool folder in Git LFS.") : new(Item.Lfs, lfs, null),
        ];
    }

    /// <summary>
    /// The pin dbchange/ledgers/toolchain.md records for this dbchange version: its latest dated row naming the version. An SSDT repository that
    /// commits no ledger is unpinned, as §17 item 1 assumes; a ledger without a row for this version, with a malformed one, or that
    /// cannot be read is an error.
    /// </summary>
    public static Result<Pin> Toolchain(string repositoryRoot, string version)
    {
        var path = Path.Combine(repositoryRoot, Ledger);
        if (!File.Exists(path))
        {
            return new Pin.Unpinned();
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("toolchain.unreadable", path + " cannot be read: " + e.Message.TrimEnd('.') + ".", "Grant this identity read access to " + Ledger + ", and close any program that holds it open.");
        }

        var ours = version.Split('+')[0];
        var row = lines.Select(line => Row.Match(line)).Where(m => m.Success && m.Groups[2].Value == ours)
            .OrderBy(m => m.Groups[1].Value, StringComparer.Ordinal).LastOrDefault();
        return row is null
            ? new Error("toolchain.unrecorded", Ledger + " has no dated row for dbchange " + ours + ".",
                "Add a row for dbchange " + ours + " to " + Ledger + ", with the Octopus step's DacFx release or UNPINNED.")
            : row.Groups[3].Value == "UNPINNED" ? new Pin.Unpinned()
            : Pin.Of(row.Groups[3].Value, row.Groups[4].Value is "" or "—" or "-" ? null : row.Groups[4].Value).Match<Result<Pin>>(pin => pin, error => error.Code == "toolchain.window-order" ? error :
                new Error("toolchain.malformed", Ledger + "'s row for dbchange " + ours + " names a pin or a release before it that is no DacFx release.",
                    "Write the row's pin and the release before it as DacFx versions, such as 170.5.96, or the pin as UNPINNED."));
    }

    /// <summary>
    /// The SDK version the nearest global.json at or above the working directory names, as dotnet itself finds it: null without one or
    /// where the file names no version; sdk.global-json for a file that is not JSON, naming the line, or that cannot be read.
    /// </summary>
    public static Result<Version?> Pinned(string workingDirectory)
    {
        var folder = Folder.Nearest(workingDirectory, directory => File.Exists(Path.Combine(directory, "global.json")));
        if (folder is null)
        {
            return Result.Ok<Version?>(null);
        }

        var file = Path.Combine(folder, "global.json");
        try
        {
            return Version.TryParse((string?)JsonNode.Parse(File.ReadAllText(file))?["sdk"]?["version"], out var version) ? version : null;
        }
        catch (JsonException e)
        {
            return new Error("sdk.global-json", file + ", which chooses the .NET SDK for this folder, is not JSON at line " + ((e.LineNumber ?? 0) + 1).ToString(CultureInfo.InvariantCulture) + ".",
                "Correct " + file + "; dotnet reads the same file and fails on it too.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("sdk.global-json", file + ", which chooses the .NET SDK for this folder, cannot be read: " + e.Message.TrimEnd('.') + ".",
                "Grant this identity read access to " + file + "; dotnet reads the same file and fails on it too.");
        }
    }

    /// <summary>Whether SQL Server Express LocalDB's default instance is installed: sqllocaldb info MSSQLLocalDB exits 0; its text, in the console's code page, is never read.</summary>
    internal static bool LocalDbInstalled(Runner run, CancellationToken cancel = default) => run(Program("sqllocaldb", "info", "MSSQLLocalDB"), cancel) is Ran.Exited { Code: 0 };

    /// <summary>
    /// The SDK, from dotnet --list-sdks through <paramref name="run"/>: without a global.json, any SDK of the runtime's major version; with
    /// one, its feature band at or above its patch (latestPatch). dotnet absent, not answering in time, or failing is named as such.
    /// </summary>
    internal static Prerequisite Sdk(string workingDirectory, Runner run, CancellationToken cancel = default)
    {
        if (Pinned(workingDirectory) is Result<Version?>.Failed { Error: var malformed })
        {
            return new(Item.Sdk, malformed.Message, malformed.Remedy);
        }

        var (pin, major) = (((Result<Version?>.Ok)Pinned(workingDirectory)).Value, Environment.Version.Major);
        var band = pin is null ? string.Create(CultureInfo.InvariantCulture, $"{major}.x") : string.Create(CultureInfo.InvariantCulture, $"{pin.Major}.{pin.Minor}.{pin.Build / 100}xx");
        var (wanted, sh, ps) = pin is null ? (string.Create(CultureInfo.InvariantCulture, $"{major}.0"), "--channel", "-Channel") : (pin.ToString(), "--version", "-Version");
        var install = "Install the .NET SDK " + wanted + ": dotnet-install.sh " + sh + " " + wanted + ", or dotnet-install.ps1 " + ps + " " + wanted + " on Windows.";
        switch (run(Program("dotnet", "--list-sdks"), cancel))
        {
            case Ran.NotFound:
                return new(Item.Sdk, "dotnet is not on the PATH", install);
            case Ran.TimedOut:
                return new(Item.Sdk, "dotnet did not answer in " + Command.Written(ProgramTimeout), "Run dotnet --list-sdks by hand to see what it waits for, then run dbchange doctor.");
            case Ran.Exited { Code: not 0 } failed:
                return new(Item.Sdk, "dotnet --list-sdks failed: " + (failed.Errors + failed.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(""), "Repair or reinstall the .NET SDK, then run dbchange doctor.");
            case Ran.Exited listed:
                var fit = listed.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => Version.TryParse(line.Split(' ')[0], out var v) ? v : null)
                    .Where(v => v is not null && (pin is null ? v.Major == major : v.Major == pin.Major && v.Minor == pin.Minor && v.Build / 100 == pin.Build / 100 && v.Build >= pin.Build))
                    .Max();
                return fit is null ? new(Item.Sdk, "no " + band + " SDK", install) : new(Item.Sdk, fit.ToString(), null);
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>Whether a folder is a published tool folder: the SqlTasks targets and the reference assemblies present; the remedy names what is absent.</summary>
    internal static Prerequisite Tool(string toolFolder)
    {
        var absent = Published.Where(file => !File.Exists(Path.Combine(toolFolder, file))).ToList();
        return absent.Count == 0
            ? new(Item.Tool, "published", null)
            : new(Item.Tool, "not a published tool folder (" + string.Join(", ", absent.Select(Path.GetFileName)) + " absent)", "ci/publish.sh, or ci/publish.ps1 on Windows, publishes dist/dbchange/; run dbchange from there");
    }

    /// <summary>The tool folder dbchange would build with (Ssdt.Tool), and whether its DacFx build task is the committed DacFx: a stale publish under another DacFx is named.</summary>
    private static Prerequisite Tool(Machine machine) => Ssdt.Tool(machine.Running, machine.Checkout.Tool, machine.Checkout.WorkingDirectory).Match(
        folder => FileVersion(Path.Combine(folder, BuildTask)) is { } task && DacFx.Version is Result<DacFxVersion>.Ok(var running) && task != running.ToString()
            ? new Prerequisite(Item.Tool, (folder == machine.Running ? "published" : folder) + ", whose DacFx build task is " + task + " while dbchange runs DacFx " + running,
                "Run ci/publish.sh, or ci/publish.ps1 on Windows, again so the tool folder carries DacFx " + running + ", then run dbchange doctor.")
            : new Prerequisite(Item.Tool, folder == machine.Running ? "published" : folder, null),
        error => new Prerequisite(Item.Tool, "missing", error.Remedy));

    /// <summary>The committed DacFx against the ledger's row: its pin, the error in the row, or the rejection of a DacFx outside the window; or why dbchange cannot name the release it runs.</summary>
    private static Prerequisite Committed(string repositoryRoot, string version) => DacFx.Version.Match(
        dacfx => Toolchain(repositoryRoot, version).Map(pin => (Pin: pin, Rejection: pin.Rejects(dacfx))).Match(
            found => new Prerequisite(Item.DacFx, dacfx + " (" + (found.Rejection is null ? found.Pin.Match(_ => "UNPINNED", pinned => "pinned " + pinned) : "outside the pin " + found.Pin) + ")", found.Rejection?.Remedy),
            error => new Prerequisite(Item.DacFx, dacfx + " (" + error.Message.TrimEnd('.') + ")", error.Remedy)),
        error => new Prerequisite(Item.DacFx, "unknown (" + error.Message.TrimEnd('.') + ")", error.Remedy));

    /// <summary>git --version: absent, not answering, or older than 2.24, which lacks the --end-of-options io/Git passes to rev-parse.</summary>
    private static Prerequisite GitVersion(Runner run, CancellationToken cancel) => run(Program("git", "--version"), cancel) switch
    {
        Ran.NotFound => new(Item.Git, "absent", "Install git and put it on the PATH, then run dbchange doctor."),
        Ran.TimedOut => new(Item.Git, "git did not answer in " + Command.Written(ProgramTimeout), "Run git --version by hand to see what it waits for, then run dbchange doctor."),
        Ran.Exited { Code: 0 } said when Version.TryParse(string.Concat((said.Output.Trim().Split(' ').ElementAtOrDefault(2) ?? "").TakeWhile(c => char.IsAsciiDigit(c) || c == '.')).TrimEnd('.'), out var version) =>
            version >= GitLeast ? new(Item.Git, version.ToString(), null)
            : new(Item.Git, "git " + version + " is older than " + GitLeast + ", which dbchange needs for rev-parse --end-of-options", "Install git " + GitLeast + " or later, then run dbchange doctor."),
        var other => new(Item.Git, "git --version did not answer as git does: " + other, "Repair or reinstall git, then run dbchange doctor."),
    };

    /// <summary>
    /// The local server dbchange would use, in io/LocalServer's order: DBCHANGE_SQL's server; the dbchange-sql container ~/.dbchange/sql.env names,
    /// which needs Docker answering; LocalDB's default instance. With none, the Docker-specific cause: not installed, its daemon not
    /// answering, docker info past its timeout, or no sql.env because ci/sql.sh up never ran.
    /// </summary>
    private static Prerequisite LocalServerChoice(Machine machine, Ran? docker, Runner run, CancellationToken cancel)
    {
        var (dockerAnswers, dockerCause) = docker switch
        {
            null => (false, null),
            Ran.Exited { Code: 0 } => (true, null),
            Ran.Exited => (false, "Docker does not answer (docker info): start Docker Desktop, or the docker service, then run dbchange doctor."),
            Ran.TimedOut => (false, "docker info did not answer in " + Command.Written(ProgramTimeout) + ": restart Docker, then run dbchange doctor."),
            _ => (false, "Install Docker, or use SQL Server Express LocalDB on Windows, then run dbchange doctor."),
        };
        var localDb = machine.DbChangeSql is { Length: > 0 } || LocalServer.Server(null, machine.SqlEnv ?? "", localDb: false) is Result<string>.Ok || !LocalDbInstalled(run, cancel) ? false : true;
        return LocalServer.Server(machine.DbChangeSql, machine.SqlEnv ?? "", localDb).Bind(server => LocalServer.ServerName(server).Map(name => (Server: server, Name: name))).Match(
            chosen => machine.DbChangeSql is { Length: > 0 } ? new Prerequisite(Item.LocalServer, "DBCHANGE_SQL (" + chosen.Name + ")", null)
                : localDb ? new Prerequisite(Item.LocalServer, "LocalDB MSSQLLocalDB, CDC not provable here", null)
                : new Prerequisite(Item.LocalServer, Container + " container (" + chosen.Name + ")", dockerAnswers ? null : dockerCause),
            error => new Prerequisite(Item.LocalServer, "none: " + (dockerAnswers ? "Docker answers, and " + (machine.SqlEnv ?? "~/.dbchange/sql.env") + " names no container" : "Docker does not answer") + ", DBCHANGE_SQL is unset and LocalDB is not installed",
                dockerAnswers ? "Run ci/sql.sh up, or ci/sql.ps1 up on Windows, which creates the " + Container + " container, then run dbchange doctor." : dockerCause ?? error.Remedy));
    }

    /// <summary>The pinned image, when Docker is the local server: present or absent; and the dbchange-sql container, when it exists, made from that image and no other.</summary>
    private static Prerequisite Image(Machine machine, Ran? docker, Runner run, CancellationToken cancel) =>
        machine.DbChangeSql is { Length: > 0 } ? new(Item.Image, "not needed: DBCHANGE_SQL names the server", null)
        : docker is not Ran.Exited { Code: 0 } ? new(Item.Image, "not needed without Docker", null)
        : run(Program("docker", "image", "inspect", "--format", "{{.Id}}", SqlServerImage), cancel) is not Ran.Exited { Code: 0 } ? new(Item.Image, "absent", "Run ci/sql.sh up, which pulls it (docker pull " + SqlServerImage + "), then run dbchange doctor.")
        : run(Program("docker", "container", "inspect", Container, "--format", "{{.Config.Image}}"), cancel) is Ran.Exited { Code: 0 } container && container.Output.Trim() is var made && made != SqlServerImage
            ? new(Item.Image, "present, and " + Container + " runs " + made, "Run ci/sql.sh down, then ci/sql.sh up (ci/sql.ps1 on Windows), so " + Container + " runs the pinned image, then run dbchange doctor.")
        : new(Item.Image, "present", null);

    private static Command Program(string program, params string[] arguments) => new(program, arguments, ProgramTimeout);

    /// <summary>A file's version as major.minor.build, or null for a file that is absent or carries none.</summary>
    private static string? FileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var v = FileVersionInfo.GetVersionInfo(path);
        return v.FileVersion is null ? null : string.Create(CultureInfo.InvariantCulture, $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}");
    }
}
