using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// Every path an engine document cites resolves. The rule, as small as it can be and still honest:
/// <list type="bullet">
/// <item>A citation is a relative markdown link, or a backticked span that is one word of path characters whose
/// first segment is a root entry of this repository: one that exists, one the plan adds later (§5.1 of the
/// instruction architecture), or a run-time root. Anything else (the estate repository's <c>estate/…</c> and
/// <c>tools/estate/</c>, a branch, a type) is not this repository's path, and the estate's pipeline checks its own.</item>
/// <item>It resolves when the file or directory exists; or when its logical line (the table row, list item or
/// paragraph) names a milestone not yet exited, as in <c>pending M3</c> or <c>from M7</c>, the milestone that
/// creates it; or when it lies under a directory the plan creates later, until that milestone's exit.</item>
/// <item>A path under a run-time root (<c>.estate/</c>, <c>dist/</c>) is never in the tree, because git ignores it,
/// and is not checked.</item>
/// </list>
/// The five root design documents are excluded until M8; knowledge/ joins at M7.
/// </summary>
public sealed class Citations
{
    private static readonly string[] PlannedRoots = ["ARCHITECTURE.md", "DOCS.md", "LAWS.md", ".ignore", "knowledge"];

    private static readonly (string Path, int Milestone)[] PlannedDirectories = [("knowledge/", 7), ("tests/Golden/", 0)];

    private static readonly string[] RunTimeRoots = [".estate", "dist"];

    private static readonly Regex Backticked = new(@"`([\w.][\w.\-/]*)`", RegexOptions.CultureInvariant);

    private static readonly Regex Link = new(@"\]\((?![a-z]+:)([^)\s#]+)", RegexOptions.CultureInvariant);

    private static readonly Regex MilestoneNamed = new(@"\bM(\d+)\b", RegexOptions.CultureInvariant);

    public static TheoryData<string> EngineDocuments => new(Documents.Engine);

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(EngineDocuments))]
    public void Every_path_an_engine_document_cites_exists_or_names_the_milestone_that_creates_it(string document)
    {
        var directory = Path.GetDirectoryName(document)!.Replace('\\', '/');
        var unresolved = Documents.Blocks(document).SelectMany(b =>
                Backticked.Matches(b.Text).Select(m => m.Groups[1].Value).Where(IsRepositoryPath)
                    .Concat(Link.Matches(b.Text).Select(m => Path.Join(directory, m.Groups[1].Value).Replace('\\', '/')))
                    .Where(p => !Resolves(p, b.Text))
                    .Select(p => document + ":" + b.Line.ToString(CultureInfo.InvariantCulture) + ": " + p + " does not exist; fix the path, or name the milestone that creates it"));

        Assert.Empty(unresolved);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("kernel/BannedSymbols.txt", true)]
    [InlineData("LAWS.md", true)]
    [InlineData(".estate/copies.json", true)]
    [InlineData("knowledge/record.md", true)]
    [InlineData("estate/ledgers/in-flight.md", false)]
    [InlineData("tools/estate/", false)]
    [InlineData("claude/v3-build", false)]
    [InlineData("try/finally", false)]
    [InlineData("--help", false)]
    [InlineData("BannedSymbolsTests", false)]
    public void A_citation_is_a_path_whose_first_segment_is_a_root_entry(string span, bool cited) =>
        Assert.Equal(cited, Backticked.IsMatch("`" + span + "`") && IsRepositoryPath(span));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("cli/VERBS.md", "`cli/VERBS.md` lists every verb.", false)]
    [InlineData("cli/VERBS.md", "From M8, `cli/VERBS.md` is generated from it.", true)]
    [InlineData("io/Substrate", "| O1 | … | `io/Substrate` over both | `Io.Tests` on both substrates — pending M3 |", true)]
    [InlineData("tests/Golden/classic-minimal/", "build `tests/Golden/classic-minimal/` on the laptop", true)]
    [InlineData(".estate/bin/", "builds `cli` into `.estate/bin/` when stale", true)]
    public void A_missing_path_resolves_only_through_a_milestone_not_yet_exited(string path, string line, bool resolves) =>
        Assert.Equal(resolves, Resolves(path, line));

    private static readonly Lazy<HashSet<string>> Roots = new(() =>
        Repository.Files.Select(f => f.Split('/')[0]).Concat(PlannedRoots).Concat(RunTimeRoots).ToHashSet(StringComparer.Ordinal));

    private static bool IsRepositoryPath(string span) => Roots.Value.Contains(span.Split('/')[0]);

    private static bool Resolves(string path, string line) =>
        RunTimeRoots.Contains(path.Split('/')[0])
        || Repository.Contains(path)
        || MilestoneNamed.Matches(line).Any(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) >= Documents.Milestone)
        || PlannedDirectories.Any(d => (path.TrimEnd('/') + "/").StartsWith(d.Path, StringComparison.Ordinal) && d.Milestone >= Documents.Milestone);
}
