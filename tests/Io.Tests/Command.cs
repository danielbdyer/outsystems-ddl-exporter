using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Estate.Budgets.Tests;

namespace Estate.Io.Tests;

/// <summary>A program run from the repository root, or the directory given, its standard output and error together, read as UTF-8; exit -1 when it is not installed or overran.</summary>
internal static class Command
{
    public static (int Exit, string Output) Run(string file, IEnumerable<string> arguments, TimeSpan? timeout = null, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Repository.Root,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        try
        {
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout ?? TimeSpan.FromMinutes(10)))
            {
                process.Kill(entireProcessTree: true);
                return (-1, file + " overran " + (timeout ?? TimeSpan.FromMinutes(10)));
            }

            return (process.ExitCode, stdout.Result + stderr.Result);
        }
        catch (Win32Exception e)
        {
            return (-1, file + " is not installed: " + e.Message);
        }
    }
}
