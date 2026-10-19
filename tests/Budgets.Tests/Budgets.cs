using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// Code, tests and the corpus stay under the ceilings in ci/budgets.json, counted in physical lines over each budget's
/// include globs, less its exclude globs and neverCounted (bin/ and obj/). A red budget names its ceiling and its
/// largest files; the fix is to shorten a file, or to raise the ceiling with a DECISIONS.md line. Each C# file also
/// holds its eachFile group's ceiling in physical lines, and a file of a group that carries a widthCeiling holds it in
/// characters on every line: the code does, the tests do not (the operator's ruling of 2026-10-19).
/// </summary>
public sealed class Budgets
{
    /// <summary>How many files or lines a red per-file budget names, largest first; the message gives the total as well.</summary>
    private const int Named = 20;

    private static readonly JsonNode Data = JsonNode.Parse(Repository.Read("ci/budgets.json"))!;

    public static TheoryData<string> Packages => new(Data["budgets"]!.AsObject().Select(b => b.Key));

    public static TheoryData<string> FileGroups => new(Data["eachFile"]!["groups"]!.AsObject().Select(g => g.Key));

    /// <summary>The eachFile groups that carry a widthCeiling; a group without one is held to its line ceiling alone.</summary>
    public static TheoryData<string> WidthGroups =>
        new(Data["eachFile"]!["groups"]!.AsObject().Where(g => g.Value!["widthCeiling"] is not null).Select(g => g.Key));

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "L3")]
    [MemberData(nameof(Packages))]
    public void Each_budget_holds_its_ceiling(string budget)
    {
        var data = Data["budgets"]![budget]!;
        var files = Counted(data);

        Assert.True(files.Sum(f => f.Lines) <= (int)data["ceiling"]!, Over("budgets." + budget, (int)data["ceiling"]!, files));
        var roots = Strings(data["include"]).Select(g => g.Split('*', '?')[0].TrimEnd('/'));
        Assert.True(files.Count > 0 || !roots.Any(Repository.Contains),
            "ci/budgets.json budgets." + budget + " counts no file under a directory that exists: its include globs are wrong");
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L3")]
    public void The_code_together_holds_its_ceiling()
    {
        var files = Strings(Data["code"]!["budgets"]).SelectMany(b => Counted(Data["budgets"]![b]!))
            .OrderByDescending(f => f.Lines).ToList();

        Assert.True(files.Sum(f => f.Lines) <= (int)Data["code"]!["ceiling"]!, Over("code", (int)Data["code"]!["ceiling"]!, files));
    }

    /// <summary>
    /// A C# file holds its group's ceiling in physical lines (500 for kernel/, io/ and cli/, 800 for tests/ outside
    /// tests/Golden/); a red group names each file over it with its count, largest first.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "L3")]
    [MemberData(nameof(FileGroups))]
    public void Each_file_of_a_group_holds_the_group_s_line_ceiling(string group)
    {
        var data = Data["eachFile"]!["groups"]![group]!;
        var ceiling = (int)data["ceiling"]!;
        var files = Counted(data);
        var over = files.Where(f => f.Lines > ceiling).ToList();

        Assert.True(files.Count > 0, "ci/budgets.json eachFile.groups." + group + " counts no file: its include globs are wrong");
        var message = string.Create(CultureInfo.InvariantCulture, $"ci/budgets.json eachFile.groups.{group}: the ceiling is {ceiling} ")
            + string.Create(CultureInfo.InvariantCulture, $"physical lines; files over it, {over.Count} of {files.Count}. ")
            + "Split each into partial files, or raise the ceiling with a DECISIONS.md line. "
            + Listed("Largest", over.Select(f => f.Path + " " + Number(f.Lines)), over.Count);
        Assert.True(over.Count == 0, message);
    }

    /// <summary>
    /// Every line of a C# file of a group that carries a widthCeiling holds it: 140 characters for kernel/, io/ and cli/;
    /// a red group names each line over it as its path:line and its width, widest first.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "L3")]
    [MemberData(nameof(WidthGroups))]
    public void Every_line_of_a_file_of_a_group_holds_the_width_ceiling(string group)
    {
        var data = Data["eachFile"]!["groups"]![group]!;
        var width = (int)data["widthCeiling"]!;
        var files = Counted(data);
        var wide = files
            .SelectMany(f => PhysicalLines(Repository.Read(f.Path)).Select((line, i) => (f.Path, Line: i + 1, Width: line.Length)))
            .Where(l => l.Width > width)
            .OrderByDescending(l => l.Width).ThenBy(l => l.Path, StringComparer.Ordinal).ThenBy(l => l.Line)
            .ToList();

        Assert.True(files.Count > 0, "ci/budgets.json eachFile.groups." + group + " counts no file: its include globs are wrong");
        var message = string.Create(CultureInfo.InvariantCulture, $"ci/budgets.json eachFile.groups.{group}.widthCeiling: the ceiling ")
            + string.Create(CultureInfo.InvariantCulture, $"is {width} characters; lines of the group over it, {wide.Count}. ")
            + "Break each without changing what it means, or raise the ceiling with a DECISIONS.md line. "
            + Listed("Widest", wide.Select(l => l.Path + ":" + Number(l.Line) + " " + Number(l.Width)), wide.Count);
        Assert.True(wide.Count == 0, message);
    }

    /// <summary>
    /// The rules both per-file ceilings count by: a line ends at '\n', a last line without one still counts, and a line's
    /// width is its length as a .NET string, so a tab is one character, a surrogate pair two, and a trailing '\r' none.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("", 0, 0)]
    [InlineData("\n", 1, 0)]
    [InlineData("one\ntwo\n", 2, 3)]
    [InlineData("one\ntwo", 2, 3)]
    [InlineData("one\r\ntwo\r\n", 2, 3)]
    [InlineData("one\rtwo\n", 1, 7)]
    [InlineData("width\r\r\n", 1, 6)]
    [InlineData("\tx\n", 1, 2)]
    [InlineData("café\n", 1, 4)]
    [InlineData("\U0001F600\n", 1, 2)]
    [InlineData("short\nthe widest line\nmid\n", 3, 15)]
    public void A_file_s_lines_and_their_widths_are_counted_as_the_per_file_ceilings_count_them(string text, int lines, int widest)
    {
        var counted = PhysicalLines(text);

        Assert.Equal(lines, counted.Count);
        Assert.Equal(widest, counted.Select(l => l.Length).DefaultIfEmpty(0).Max());
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("kernel/SortedArray.cs", "kernel/**/*.cs", true)]
    [InlineData("kernel/SyntheticData/Generate.cs", "kernel/**/*.cs", true)]
    [InlineData("kernel/SortedArray.csx", "kernel/**/*.cs", false)]
    [InlineData("kernelx/SortedArray.cs", "kernel/**/*.cs", false)]
    [InlineData("kernel/obj/Debug/net10.0/DbChange.Kernel.AssemblyInfo.cs", "**/obj/**", true)]
    [InlineData("tests/Golden/classic-minimal/Estate.sqlproj", "tests/Golden/**", true)]
    [InlineData("tests/Kernel.Tests/SortedArrayTests.cs", "tests/Golden/**", false)]
    public void A_glob_matches_the_paths_it_names(string path, string glob, bool matches) =>
        Assert.Equal(matches, Repository.Glob(glob).IsMatch(path));

    /// <summary>The files a budget counts, largest first, each with its physical lines.</summary>
    internal static IReadOnlyList<(string Path, int Lines)> Counted(JsonNode budget)
    {
        var include = Strings(budget["include"]).Select(Repository.Glob).ToList();
        var exclude = Strings(budget["exclude"]).Concat(Strings(Data["neverCounted"])).Select(Repository.Glob).ToList();
        return Repository.Files
            .Where(f => include.Any(g => g.IsMatch(f)) && !exclude.Any(g => g.IsMatch(f)))
            .Select(f => (f, Repository.PhysicalLines(f)))
            .OrderByDescending(f => f.Item2)
            .ThenBy(f => f.f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A text's physical lines, each without its '\n' and without one '\r' before it; a last line without '\n' counts.</summary>
    internal static IReadOnlyList<string> PhysicalLines(string text)
    {
        string[] lines = text.Length == 0 ? [] : text.Split('\n');
        return lines.Take(text.EndsWith('\n') ? lines.Length - 1 : lines.Length)
            .Select(l => l.EndsWith('\r') ? l[..^1] : l)
            .ToList();
    }

    private static IEnumerable<string> Strings(JsonNode? array) => array?.AsArray().Select(x => (string)x!) ?? [];

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>What a red per-file budget lists, in the order named: the first <see cref="Named"/> and how many in all.</summary>
    private static string Listed(string order, IEnumerable<string> listed, int total) =>
        (total > Named ? "The first " + Number(Named) + " of " + Number(total) + ", " + order.ToLowerInvariant() : order)
        + " first: " + string.Join(", ", listed.Take(Named));

    private static string Over(string key, int ceiling, IReadOnlyList<(string Path, int Lines)> files) =>
        string.Create(CultureInfo.InvariantCulture,
            $"ci/budgets.json {key}: {files.Sum(f => f.Lines)} physical lines against the ceiling of {ceiling}. Shorten a file, ")
            + "or raise the ceiling with a DECISIONS.md line. Largest first: "
            + string.Join(", ", files.Take(5).Select(f => f.Path + " " + f.Lines.ToString(CultureInfo.InvariantCulture)));
}
