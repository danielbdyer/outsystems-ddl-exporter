using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// Code, tests and the corpus stay under the ceilings in ci/budgets.json, counted in physical lines over each budget's
/// include globs, less its exclude globs and neverCounted (bin/ and obj/). A red budget names its ceiling and its
/// largest files; the fix is to shorten a file, or to raise the ceiling with a DECISIONS.md line.
/// </summary>
public sealed class Budgets
{
    private static readonly JsonNode Data = JsonNode.Parse(Repository.Read("ci/budgets.json"))!;

    public static TheoryData<string> Packages => new(Data["budgets"]!.AsObject().Select(b => b.Key));

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Packages))]
    public void Each_budget_holds_its_ceiling(string budget)
    {
        var data = Data["budgets"]![budget]!;
        var files = Counted(data);

        Assert.True(files.Sum(f => f.Lines) <= (int)data["ceiling"]!, Over("budgets." + budget, (int)data["ceiling"]!, files));
        var roots = Strings(data["include"]).Select(g => g.Split('*', '?')[0].TrimEnd('/'));
        Assert.True(files.Count > 0 || !roots.Any(Repository.Contains), "ci/budgets.json budgets." + budget + " counts no file under a directory that exists: its include globs are wrong");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_code_together_holds_its_ceiling()
    {
        var files = Strings(Data["code"]!["budgets"]).SelectMany(b => Counted(Data["budgets"]![b]!)).OrderByDescending(f => f.Lines).ToList();

        Assert.True(files.Sum(f => f.Lines) <= (int)Data["code"]!["ceiling"]!, Over("code", (int)Data["code"]!["ceiling"]!, files));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("kernel/Seq.cs", "kernel/**/*.cs", true)]
    [InlineData("kernel/Twin/Synth.cs", "kernel/**/*.cs", true)]
    [InlineData("kernel/Seq.csx", "kernel/**/*.cs", false)]
    [InlineData("kernelx/Seq.cs", "kernel/**/*.cs", false)]
    [InlineData("kernel/obj/Debug/net10.0/Estate.Kernel.AssemblyInfo.cs", "**/obj/**", true)]
    [InlineData("tests/Golden/classic-minimal/Estate.sqlproj", "tests/Golden/**", true)]
    [InlineData("tests/Kernel.Tests/SeqTests.cs", "tests/Golden/**", false)]
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

    private static IEnumerable<string> Strings(JsonNode? array) => array?.AsArray().Select(x => (string)x!) ?? [];

    private static string Over(string key, int ceiling, IReadOnlyList<(string Path, int Lines)> files) => string.Create(CultureInfo.InvariantCulture,
        $"ci/budgets.json {key}: {files.Sum(f => f.Lines)} physical lines against the ceiling of {ceiling}. Shorten a file, or raise the ceiling with a DECISIONS.md line. Largest first: ")
        + string.Join(", ", files.Take(5).Select(f => f.Path + " " + f.Lines.ToString(CultureInfo.InvariantCulture)));
}
