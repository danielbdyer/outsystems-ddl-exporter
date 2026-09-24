using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;

namespace Estate.Io;

/// <summary>
/// Can this machine do the work, read-only (V3_ARCHITECTURE.md §8.12): the .NET SDK in the band global.json names and the runtime;
/// the committed tool folder and its DacFx against the estate's toolchain ledger; the build route; a substrate (Docker answering, or
/// LocalDB) and the pinned SQL Server image; and Git LFS. Each item missing carries its remedy.
/// </summary>
public static class Doctor
{
    /// <summary>The substrate's image, pinned by tag and digest (§1 fact 12); ci/sql.sh and ci/sql.ps1 run the same one.</summary>
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    /// <summary>The toolchain ledger, from the estate's root: one dated row per estate version, with the pinned engine or UNPINNED.</summary>
    public const string Ledger = "estate/ledgers/toolchain.md";

    /// <summary>What the check of one item found, and its remedy when the item is missing.</summary>
    public sealed record Check(string Item, string Found, string? Remedy);

    /// <summary>A program's exit code and output, or null when it is not installed or does not answer in time.</summary>
    public delegate (int Exit, string Output)? Command(string file, IReadOnlyList<string> arguments);

    /// <summary>The files a published tool folder holds beside estate: DacFx's SqlTasks targets and the reference stub.</summary>
    private static readonly string[] Published = ["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"];

    /// <summary>A ledger row: | date | estate version | pinned DacFx or UNPINNED | the release before the pin, or — |.</summary>
    private static readonly Regex Row = new(@"^\|\s*(\d{4}-\d{2}-\d{2})\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|\s*$", RegexOptions.CultureInvariant);

    /// <summary>The committed engine: the DacFx release estate runs, its package version as in 170.5.96.</summary>
    public static string DacFx { get; } = FileVersionInfo.GetVersionInfo(typeof(DacServices).Assembly.Location) is var v
        ? string.Create(CultureInfo.InvariantCulture, $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}") : "";

    /// <summary>The pinned image's digest, which a receipt stamps when a copy ran in the container.</summary>
    public static string ImageDigest => SqlServerImage[(SqlServerImage.IndexOf('@', StringComparison.Ordinal) + 1)..];

    public static IReadOnlyList<Check> Examine(string version) =>
        Examine(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("ESTATE_TOOL"), Directory.GetCurrentDirectory(), Run, version);

    public static IReadOnlyList<Check> Examine(string toolFolder, string? toolVariable, string workingDirectory, Command run, string version)
    {
        var docker = run("docker", ["info", "--format", "{{.ServerVersion}}"]) is (0, var answer) ? answer.Trim() : null;
        var (sdk, tool) = (Sdk(workingDirectory, run), Ssdt.Tool(toolFolder, toolVariable, workingDirectory).Match(
            folder => new Check("tool", folder == toolFolder ? "published" : folder, null), refusal => new Check("tool", "missing", refusal.Remedy)));
        var lfs = run("git", ["lfs", "version"]) is (0, var said) ? said.Trim().Split(' ')[0] : null;
        return
        [
            sdk, new("runtime", Environment.Version.ToString(), null), tool, Committed(Profiles.Root(workingDirectory), version),
            sdk.Remedy is null && tool.Remedy is null ? new("build", "dotnet with the tool folder's targets", null)
                : new("build", "none", "install what the sdk and tool items name; then estate doctor"),
            Substrate(docker, run), Image(docker, run),
            lfs is null ? new("lfs", "absent", "install Git LFS, then run git lfs install; the estate's evidence needs it from M5") : new("lfs", lfs, null),
        ];
    }

    /// <summary>
    /// The pin estate/ledgers/toolchain.md records for this estate version: its latest dated row naming the version. An estate that
    /// commits no ledger is unpinned, as §17 item 1 assumes; a ledger without a row for this version, or with a malformed one, is refused.
    /// </summary>
    public static Result<Pin> Toolchain(string estateRoot, string version)
    {
        var path = Path.Combine(estateRoot, Ledger);
        if (!File.Exists(path))
        {
            return new Pin.Unpinned();
        }

        var ours = version.Split('+')[0];
        var row = File.ReadAllLines(path).Select(line => Row.Match(line)).Where(m => m.Success && m.Groups[2].Value == ours)
            .OrderBy(m => m.Groups[1].Value, StringComparer.Ordinal).LastOrDefault();
        return row is null
            ? new Refusal("toolchain.unrecorded", Ledger + " has no dated row for estate " + ours + ".",
                "Add a row for estate " + ours + " to " + Ledger + ", with the Octopus step's DacFx release or UNPINNED.")
            : row.Groups[3].Value == "UNPINNED" ? new Pin.Unpinned()
            : Pin.Of(row.Groups[3].Value, row.Groups[4].Value is "" or "—" or "-" ? null : row.Groups[4].Value).Match<Result<Pin>>(pin => pin, refusal => refusal.Code == "toolchain.window-order" ? refusal :
                new Refusal("toolchain.malformed", Ledger + "'s row for estate " + ours + " names a pin or a release before it that is no DacFx release.",
                    "Write the row's pin and the release before it as DacFx versions, such as 170.5.96, or the pin as UNPINNED."));
    }

    /// <summary>The committed DacFx against the ledger's row: its pin, or the refusal of the row or of an engine outside the window.</summary>
    private static Check Committed(string estateRoot, string version) => Toolchain(estateRoot, version)
        .Bind(pin => Engine.Of(DacFx).Map(engine => (Pin: pin, Refusal: pin.Refuses(engine))))
        .Match(
            found => new Check("dacfx", DacFx + " (" + (found.Refusal is null ? found.Pin.Match(_ => "UNPINNED", pinned => "pinned " + pinned) : "outside the pin " + found.Pin) + ")", found.Refusal?.Remedy),
            refusal => new Check("dacfx", DacFx + " (" + refusal.Message.TrimEnd('.') + ")", refusal.Remedy));

    internal static Check Sdk(string workingDirectory, Command run)
    {
        // Without a global.json, any SDK of the runtime's major version; with one, its feature band at or above its patch (latestPatch).
        var (pin, major) = (Pinned(workingDirectory), Environment.Version.Major);
        var band = pin is null ? string.Create(CultureInfo.InvariantCulture, $"{major}.x") : string.Create(CultureInfo.InvariantCulture, $"{pin.Major}.{pin.Minor}.{pin.Build / 100}xx");
        var fit = (run("dotnet", ["--list-sdks"])?.Output ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Version.TryParse(line.Split(' ')[0], out var v) ? v : null)
            .Where(v => v is not null && (pin is null ? v.Major == major : v.Major == pin.Major && v.Minor == pin.Minor && v.Build / 100 == pin.Build / 100 && v.Build >= pin.Build))
            .Max();
        var (wanted, sh, ps) = pin is null ? (string.Create(CultureInfo.InvariantCulture, $"{major}.0"), "--channel", "-Channel") : (pin.ToString(), "--version", "-Version");
        return fit is null
            ? new("sdk", "no " + band + " SDK", "install the .NET SDK " + wanted + ": dotnet-install.sh " + sh + " " + wanted + ", or dotnet-install.ps1 " + ps + " " + wanted + " on Windows")
            : new("sdk", fit.ToString(), null);
    }

    /// <summary>The SDK version the nearest global.json at or above the working directory names, as dotnet itself finds it.</summary>
    private static Version? Pinned(string workingDirectory)
    {
        for (var directory = new DirectoryInfo(workingDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(file))
            {
                return Version.TryParse((string?)JsonNode.Parse(File.ReadAllText(file))?["sdk"]?["version"], out var version) ? version : null;
            }
        }

        return null;
    }

    internal static Check Tool(string toolFolder)
    {
        var absent = Published.Where(file => !File.Exists(Path.Combine(toolFolder, file))).ToList();
        return absent.Count == 0
            ? new("tool", "published", null)
            : new("tool", "not a published tool folder (" + string.Join(", ", absent.Select(Path.GetFileName)) + " absent)", "ci/publish.sh, or ci/publish.ps1 on Windows, publishes dist/estate/; run estate from there");
    }

    private static Check Substrate(string? docker, Command run) =>
        docker is not null ? new("substrate", "docker " + docker, null)
        : run("sqllocaldb", ["info"]) is (0, _) ? new("substrate", "localdb, CDC not provable here", null)
        : new("substrate", "none: Docker does not answer and LocalDB is absent", "start Docker until docker info answers; where Docker cannot run, install SQL Server Express LocalDB");

    private static Check Image(string? docker, Command run) =>
        docker is null ? new("image", "not needed without Docker", null)
        : run("docker", ["image", "inspect", "--format", "{{.Id}}", SqlServerImage]) is (0, _) ? new("image", "present", null)
        : new("image", "absent", "ci/sql.sh up pulls it (docker pull " + SqlServerImage + ")");

    /// <summary>Runs a program read-only, its standard error discarded; null when it is not installed or overruns twenty seconds.</summary>
    public static (int Exit, string Output)? Run(string file, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true };
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (process.WaitForExit(TimeSpan.FromSeconds(20)))
            {
                return (process.ExitCode, output.Result);
            }

            process.Kill(entireProcessTree: true);
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
