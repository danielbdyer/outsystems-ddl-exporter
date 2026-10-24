using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests;

public class FilesystemPermissionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildSsdt_fails_when_output_directory_is_read_only()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));

        using var output = new TempDirectory();
        var outputPath = output.Path;

        FileAttributes? originalAttributes = null;
        string? originalMode = null;

        try
        {
            SetReadOnly(outputPath, ref originalAttributes, ref originalMode);

            var command =
                $"run --project {cliProject} -- build-ssdt --model {modelPath} --profile {profilePath} --static-data {staticDataPath} --out {outputPath}";
            var result = await DotNetCli.RunAsync(repoRoot, command).ConfigureAwait(false);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("pipeline.buildSsdt.output.permissionDenied", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RestorePermissions(outputPath, originalAttributes, originalMode);
        }
    }

    private static void SetReadOnly(
        string directory,
        ref FileAttributes? attributes,
        ref string? mode)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new DirectoryInfo(directory);
            attributes = info.Attributes;
            info.Attributes |= FileAttributes.ReadOnly;
            return;
        }

        mode = GetUnixFileMode(directory);
        RunProcess("chmod", "a-w", directory);
    }

    private static void RestorePermissions(
        string directory,
        FileAttributes? attributes,
        string? mode)
    {
        if (OperatingSystem.IsWindows())
        {
            if (attributes is { } value)
            {
                var info = new DirectoryInfo(directory);
                info.Attributes = value;
            }

            return;
        }

        if (mode is { } unixMode)
        {
            RunProcess("chmod", unixMode, directory);
        }
    }

    private static string GetUnixFileMode(string directory)
    {
        if (OperatingSystem.IsLinux())
        {
            return RunProcess("stat", "-c", "%a", directory).Trim();
        }

        if (OperatingSystem.IsMacOS())
        {
            return RunProcess("stat", "-f", "%Mp%Lp", directory).Trim();
        }

        throw new PlatformNotSupportedException("Unix permissions are only supported on Linux and macOS.");
    }

    private static string RunProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }
}
