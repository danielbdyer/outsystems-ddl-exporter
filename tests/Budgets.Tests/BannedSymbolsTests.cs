using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Estate.Tests;
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
    [Trait("Category", "build")]
    [Trait("Law", "the kernel cannot do I/O")]
    [Trait("Value", "D2")]
    [Trait("Value", "X5")]
    [Trait("Value", "O12")]
    [Trait("Value", "L7")]
    public void Each_banned_symbol_planted_in_the_kernel_is_a_build_error()
    {
        using var plant = ScratchFolder.UnderRepository("plant");
        File.Copy(Path.Combine(Repository.Root, "kernel", "kernel.csproj"), plant.Under("kernel.csproj"));
        File.Copy(Path.Combine(Repository.Root, "kernel", "BannedSymbols.txt"), plant.Under("BannedSymbols.txt"));
        var lines = new List<string> { "namespace Planted;", "", "public static class Uses", "{", "    public static object[] All() =>", "    [" };
        lines.AddRange(Planted.Select(p => $"        {p.Use},"));
        lines.AddRange(["    ];", "}", ""]);
        plant.File("Planted.cs", string.Join('\n', lines));

        var (exit, output) = Programs.InRepository("dotnet", "build", plant.Under("kernel.csproj"), "-nologo", "-v", "q", "-clp:NoSummary").Finish().Joined();

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

    /// <summary>io's own list (io/BannedSymbols.txt, wired by io.csproj) keeps Process, ProcessStartInfo and Console out of io, and only io/Command.cs, the one place io starts a program, suppresses the rule.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Io_bans_Process_and_Console_and_only_Command_suppresses_the_rule()
    {
        var banned = Repository.Lines("io/BannedSymbols.txt").Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split(';')[0]).Order(StringComparer.Ordinal);
        var suppressing = Repository.Files.Where(f => f.StartsWith("io/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal) && Repository.Read(f).Contains("RS0030", StringComparison.Ordinal));

        Assert.Equal(["T:System.Console", "T:System.Diagnostics.Process", "T:System.Diagnostics.ProcessStartInfo"], banned);
        Assert.Contains("BannedSymbols.txt", Repository.Xml("io/io.csproj").Descendants().Single(e => e.Name.LocalName == "EstateBannedSymbols").Value, StringComparison.Ordinal);
        Assert.Equal(["io/Command.cs"], suppressing);
    }

    /// <summary>
    /// The analyzer bans a type whole, so the file rules are a source scan: outside io/Write.cs no io file writes, creates, replaces, moves or
    /// copies a file (VALUES.md D3), and outside io/FileLock.cs and io/Write.cs none opens a file for itself alone (R6).
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Only_Write_writes_a_file_and_only_FileLock_and_Write_open_one_exclusively()
    {
        var writes = new Regex(@"\bFile\.(WriteAll\w*|AppendAll\w*|Create|CreateText|OpenWrite|Replace|Move|Copy)\(", RegexOptions.CultureInvariant);
        var exclusive = new Regex(@"\bFileShare\.None\b", RegexOptions.CultureInvariant);
        var offending = Repository.Files
            .Where(f => f.StartsWith("io/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => Repository.Lines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => (writes.IsMatch(x.Text) && x.File != "io/Write.cs") || (exclusive.IsMatch(x.Text) && x.File is not ("io/Write.cs" or "io/FileLock.cs")))
            .Select(x => x.File + ":" + x.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + x.Text.Trim());

        Assert.Empty(offending);
    }
}
