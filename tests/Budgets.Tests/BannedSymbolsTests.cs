using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// The kernel is pure by the absence of a capability: every symbol in kernel/BannedSymbols.txt,
/// planted in a copy of the kernel project, is a build error on the line that plants it.
/// </summary>
public sealed class BannedSymbolsTests
{
    // One planted use per banned symbol, one per line, so each error is attributable by its line.
    private static readonly (string Symbol, string Use)[] Planted =
    [
        ("N:System.IO", "System.IO.Path.GetTempPath()"),
        ("N:System.Net", "System.Net.IPAddress.Loopback"),
        ("P:System.DateTime.Now", "System.DateTime.Now"),
        ("P:System.DateTime.UtcNow", "System.DateTime.UtcNow"),
        ("P:System.DateTimeOffset.Now", "System.DateTimeOffset.Now"),
        ("P:System.DateTimeOffset.UtcNow", "System.DateTimeOffset.UtcNow"),
        ("P:System.DateTime.Today", "System.DateTime.Today"),
        ("P:System.TimeProvider.System", "System.TimeProvider.System"),
        ("M:System.Guid.NewGuid", "System.Guid.NewGuid()"),
        ("T:System.Random", "new System.Random(1)"),
        ("T:System.Security.Cryptography.RandomNumberGenerator", "System.Security.Cryptography.RandomNumberGenerator.Create()"),
        ("T:System.Diagnostics.Stopwatch", "System.Diagnostics.Stopwatch.StartNew()"),
        ("T:System.Environment", "System.Environment.ProcessorCount"),
        ("T:System.Console", "System.Console.Out"),
        ("T:System.Diagnostics.Process", "System.Diagnostics.Process.GetCurrentProcess()"),
        ("P:System.Globalization.CultureInfo.CurrentCulture", "System.Globalization.CultureInfo.CurrentCulture"),
        ("T:System.Threading.Tasks.Task", "System.Threading.Tasks.Task.CompletedTask"),
        ("T:System.Threading.Tasks.Task`1", "System.Threading.Tasks.Task<int>.Factory"),
        ("T:System.Threading.Thread", "System.Threading.Thread.CurrentThread"),
    ];

    private const int FirstPlantedLine = 6;

    [Fact]
    [Trait("Category", "fast")]
    public void The_banned_list_is_exactly_the_planted_list()
    {
        var banned = File.ReadAllLines(Path.Combine(Repository.Root, "kernel", "BannedSymbols.txt"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(';')[0])
            .Order(StringComparer.Ordinal);
        Assert.Equal(Planted.Select(p => p.Symbol).Order(StringComparer.Ordinal), banned);
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "the kernel cannot do I/O")]
    public void Each_banned_symbol_planted_in_the_kernel_is_a_build_error()
    {
        var plant = Path.Combine(Repository.Root, ".estate", "plant", $"kernel-{Environment.ProcessId}");
        Directory.CreateDirectory(plant);
        try
        {
            File.Copy(Path.Combine(Repository.Root, "kernel", "kernel.csproj"), Path.Combine(plant, "kernel.csproj"));
            File.Copy(Path.Combine(Repository.Root, "kernel", "BannedSymbols.txt"), Path.Combine(plant, "BannedSymbols.txt"));
            var lines = new List<string> { "namespace Planted;", "", "public static class Uses", "{", "    public static object[] All() =>", "    [" };
            lines.AddRange(Planted.Select(p => $"        {p.Use},"));
            lines.AddRange(["    ];", "}", ""]);
            File.WriteAllText(Path.Combine(plant, "Planted.cs"), string.Join('\n', lines));

            var (exit, output) = Run("dotnet", $"build \"{Path.Combine(plant, "kernel.csproj")}\" -nologo -v q -clp:NoSummary");

            Assert.NotEqual(0, exit);
            var errorLines = Regex.Matches(output, @"Planted\.cs\((\d+),\d+\): error RS0030")
                .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToHashSet();
            var missing = Planted
                .Select((p, i) => (p.Symbol, Line: FirstPlantedLine + 1 + i))
                .Where(p => !errorLines.Contains(p.Line))
                .Select(p => p.Symbol)
                .ToList();
            Assert.True(missing.Count == 0, $"not refused by the build: {string.Join(", ", missing)}\n{output}");
        }
        finally
        {
            Directory.Delete(plant, recursive: true);
        }
    }

    private static (int Exit, string Output) Run(string file, string arguments)
    {
        var start = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Repository.Root,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
