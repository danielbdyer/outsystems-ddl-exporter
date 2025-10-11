using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Cli.Tests;

internal static class CliTestHost
{
    private static readonly ConcurrentDictionary<string, string> CliDllCache = new();

    internal static async Task<CliRunResult> RunAsync(
        string repositoryRoot,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var cliDllPath = CliDllCache.GetOrAdd(repositoryRoot, LocateCliDll);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.Environment["DOTNET_CLI_DISABLE_TERMINAL_LOGGER"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        var cliArguments = string.IsNullOrWhiteSpace(arguments)
            ? string.Empty
            : $" {arguments}";
        startInfo.Arguments = $"exec \"{cliDllPath}\"{cliArguments}";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the CLI process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var standardOutput = await stdoutTask.ConfigureAwait(false);
        var standardError = await stderrTask.ConfigureAwait(false);

        return new CliRunResult(process.ExitCode, standardOutput, standardError);
    }

    private static string LocateCliDll(string repositoryRoot)
    {
        var cliProjectDirectory = Path.Combine(repositoryRoot, "src", "Osm.Cli");
        var configuration = GetAssemblyConfiguration();

        foreach (var candidate in EnumerateCandidatePaths(cliProjectDirectory, configuration))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Unable to locate Osm.Cli.dll for configuration '{configuration}'. Ensure the CLI project is built before running the tests.",
            "Osm.Cli.dll");
    }

    private static string GetAssemblyConfiguration()
    {
        return typeof(CliTestHost).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration
            ?? "Debug";
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string cliProjectDirectory, string configuration)
    {
        var targetFramework = "net9.0";

        yield return Path.Combine(cliProjectDirectory, "bin", configuration, targetFramework, "Osm.Cli.dll");

        var alternateConfiguration = string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        yield return Path.Combine(cliProjectDirectory, "bin", alternateConfiguration, targetFramework, "Osm.Cli.dll");

        foreach (var dll in Directory.EnumerateFiles(cliProjectDirectory, "Osm.Cli.dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            yield return dll;
        }
    }
}

internal readonly record struct CliRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void EnsureSuccess()
    {
        if (ExitCode != 0)
        {
            throw new InvalidOperationException($"CLI exited with code {ExitCode}:{Environment.NewLine}{StandardError}");
        }
    }
}
