using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Estate.Io;

/// <summary>
/// Can this machine do M0's part of the work, read-only: the .NET SDK in the band global.json names, the published tool
/// folder beside the running estate, a substrate (Docker answering, or LocalDB), and the pinned SQL Server image. The
/// engine against the toolchain ledger, the Twin and the estate checkout are M1's (WP 1.7).
/// </summary>
public static class Doctor
{
    /// <summary>The substrate's image, pinned by tag and digest (§1 fact 12); ci/sql.sh and ci/sql.ps1 run the same one.</summary>
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    /// <summary>What the check of one item found, and its remedy when the item is missing.</summary>
    public sealed record Check(string Item, string Found, string? Remedy);

    /// <summary>A program's exit code and output, or null when it is not installed or does not answer in time.</summary>
    public delegate (int Exit, string Output)? Command(string file, IReadOnlyList<string> arguments);

    /// <summary>The files a published tool folder holds beside estate: DacFx's SqlTasks targets and the reference stub.</summary>
    private static readonly string[] Published = ["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"];

    public static IReadOnlyList<Check> Examine() => Examine(AppContext.BaseDirectory, Directory.GetCurrentDirectory(), Run);

    public static IReadOnlyList<Check> Examine(string toolFolder, string workingDirectory, Command run)
    {
        var docker = run("docker", ["info", "--format", "{{.ServerVersion}}"]) is (0, var version) ? version.Trim() : null;
        return [Sdk(workingDirectory, run), Tool(toolFolder), Substrate(docker, run), Image(docker, run)];
    }

    private static Check Sdk(string workingDirectory, Command run)
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

    private static Check Tool(string toolFolder)
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
    private static (int Exit, string Output)? Run(string file, IReadOnlyList<string> arguments)
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
