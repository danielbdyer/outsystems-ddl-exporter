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
/// <item>A citation is a relative markdown link, or a backticked span of one word of path characters that holds a '/'
/// or ends in an extension this repository's files carry. Only the spans named in <see cref="Elsewhere"/> are skipped:
/// another repository's paths (the estate's pipeline checks its own), a branch, and a C# construct. So a misspelt root file
/// or directory (<c>DECISION.md</c>, <c>kernal/</c>) is a citation, and it fails.</item>
/// <item>It resolves when the file or directory exists, a bare file name when some file outside archive/ carries that
/// name; when it lies under a run-time root or is a file a verb writes at run time, which git never holds; when its
/// own clause says the milestone that creates it (<c>pending M3</c>, <c>from M7</c>, <c>at M8</c>) and that milestone
/// has not exited; or when it lies under a directory the plan creates, until that milestone's exit.</item>
/// <item>A clause ends at a ';', at a sentence's end and at a table cell's edge, so a milestone elsewhere on the line
/// excuses nothing. <c>until M8</c> and <c>M0's exit</c> never excuse a path: neither says the path arrives. Nor does
/// any milestone excuse a missing path one edit from an existing one, which is a misspelling.</item>
/// </list>
/// The five root design documents are excluded until M8; knowledge/ joins at M7.
/// </summary>
public sealed class Citations
{
    /// <summary>The extensions this repository's files carry; a bare name ending in one is a file the tree should hold.</summary>
    private static readonly string[] Extensions = [".md", ".json", ".cs", ".csproj", ".props", ".sh", ".txt", ".sln", ".allow"];

    /// <summary>
    /// Path-shaped spans that are not this repository's: the estate repository's files (<c>estate/…</c> and
    /// <c>tools/estate/</c>), the corporate agent's <c>STATE.md</c>, a branch (<c>claude/…</c>, <c>wp/…</c>), and
    /// <c>try/finally</c>, which is C#. Each is a prefix; nothing else is skipped.
    /// </summary>
    private static readonly string[] Elsewhere = ["estate/", "tools/estate/", "STATE.md", "claude/", "wp/", "try/finally"];

    /// <summary>Where the tool writes at run time: git ignores the roots, and a verb writes the files wherever --out says.</summary>
    private static readonly string[] RunTime = [".estate/", "dist/", "gate.json", "changelog.json"];

    private static readonly (string Path, int Milestone)[] PlannedDirectories = [("knowledge/", 7), ("tests/Golden/", 0)];

    private static readonly Regex Backticked = new(@"`([\w.][\w.\-/]*)`", RegexOptions.CultureInvariant);

    private static readonly Regex Link = new(@"\]\((?![a-z]+:)([^)\s#]+)", RegexOptions.CultureInvariant);

    /// <summary>A clause's edge outside backticks: a ';', a table cell's '|', or the space after a sentence's end.</summary>
    private static readonly Regex ClauseEdge = new(@"(?:;|\||(?<=[.!?])\s)(?=(?:[^`]*`[^`]*`)*[^`]*$)", RegexOptions.CultureInvariant);

    /// <summary>A milestone that creates what its clause cites: pending M3, from M7, at M8; never M0's exit.</summary>
    private static readonly Regex Arrives = new(@"\b(?:[Pp]ending|[Ff]rom|[Aa]t) M(\d+)\b(?!['’]s\b)", RegexOptions.CultureInvariant);

    public static TheoryData<string> EngineDocuments => new(Documents.Engine);

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(EngineDocuments))]
    public void Every_path_an_engine_document_cites_exists_or_names_the_milestone_that_creates_it(string document)
    {
        var directory = Path.GetDirectoryName(document)!.Replace('\\', '/');
        var unresolved = Documents.Blocks(document).SelectMany(b => Unresolved(b.Text, directory, Documents.Milestone)
            .Select(p => document + ":" + b.Line.ToString(CultureInfo.InvariantCulture) + ": " + p + " does not exist; fix the path, or say in its clause the milestone that creates it"));

        Assert.Empty(unresolved);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("kernel/BannedSymbols.txt", true)]
    [InlineData("kernal/", true)]
    [InlineData("DECISION.md", true)]
    [InlineData("LAWS.md", true)]
    [InlineData("packages.lock.json", true)]
    [InlineData(".estate/copies.json", true)]
    [InlineData("knowledge/record.md", true)]
    [InlineData("estate/ledgers/in-flight.md", false)]
    [InlineData("tools/estate/", false)]
    [InlineData("STATE.md", false)]
    [InlineData("claude/v3-build", false)]
    [InlineData("wp/0.6", false)]
    [InlineData("try/finally", false)]
    [InlineData("--help", false)]
    [InlineData("BannedSymbolsTests", false)]
    [InlineData("System.Net", false)]
    [InlineData("Io.Tests", false)]
    [InlineData("master.dacpac", false)]
    public void A_citation_is_any_path_shaped_span_not_named_as_elsewhere(string span, bool cited) =>
        Assert.Equal(cited, Backticked.IsMatch("`" + span + "`") && IsCitation(span));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`DECISION.md` is the log, one line per decision.", 0, "DECISION.md")]
    [InlineData("- `kernal/` — the domain as types and pure functions.", 0, "kernal/")]
    [InlineData("`cli/VERBS.md` lists every verb.", 0, "cli/VERBS.md")]
    [InlineData("From M8, `cli/VERBS.md` is generated from it.", 0, "")]
    [InlineData("From M8, `cli/VERBS.md` is generated from it.", 8, "")]
    [InlineData("From M8, `cli/VERBS.md` is generated from it.", 9, "cli/VERBS.md")]
    [InlineData("`cli/CONFIG.md` lists every key. From M8 the build generates it.", 0, "cli/CONFIG.md")]
    [InlineData("`LAWS.md` lists the laws. From M1 the build generates it.", 0, "")]
    [InlineData("indexed in `archive/INDEX.md`, which stays readable until M8", 0, "")]
    [InlineData("indexed in `archive/INDX.md`, which stays readable until M8", 0, "archive/INDX.md")]
    [InlineData("the operator installs `.claude/settings.json` before M0's exit", 0, "")]
    [InlineData("the operator installs `.claude/setings.json` before M0's exit", 0, ".claude/setings.json")]
    [InlineData("the operator installs `.claude/setings.json` at M0's exit", 0, ".claude/setings.json")]
    [InlineData("the operator adds `.claude/hooks/` at M0 with `.claude/settings.json`", 0, "")]
    [InlineData("the operator adds `.claude/hooks/` at M0 with `.claude/setings.json`", 0, ".claude/setings.json")]
    [InlineData("From M1, `DECISION.md` is the log.", 0, "DECISION.md")]
    [InlineData("from M3, `kernel/BannedSymbosl.txt` bans the clock", 0, "kernel/BannedSymbosl.txt")]
    [InlineData("| D2 | … | `CultureInfo.CurrentCulture` is in `kernel/BannedSymbols.txt`; law 1′ runs under `tr-TR` | `BannedSymbolsTests`; `Kernel.Tests: \"law 1′ under tr-TR\"` — pending M3 |", 0, "")]
    [InlineData("| D2 | … | `CultureInfo.CurrentCulture` is in `kernel/BannedSymbol.txt`; law 1′ runs under `tr-TR` | `BannedSymbolsTests`; `Kernel.Tests: \"law 1′ under tr-TR\"` — pending M3 |", 0, "kernel/BannedSymbol.txt")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs` over both | `Io.Tests` on both scratch servers — pending M3 |", 0, "io/SyntheticCopy.cs")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs` over both, from M3 | `Io.Tests` on both scratch servers — pending M3 |", 0, "")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs` over both, from M3 | `Io.Tests` on both scratch servers — pending M3 |", 4, "io/SyntheticCopy.cs")]
    [InlineData("extract the guards into `tests/Golden/guards/`", 0, "")]
    [InlineData("extract the guards into `tests/Golden/guards/`", 1, "tests/Golden/guards/")]
    [InlineData("builds `cli` into `.estate/bin/` when stale; no `gate.json` carries a password", 0, "")]
    [InlineData("`packages.lock.json` in every project", 0, "")]
    public void A_missing_path_resolves_only_through_a_milestone_its_own_clause_names(string line, int milestone, string unresolved) =>
        Assert.Equal(unresolved, string.Join(", ", Unresolved(line, "", milestone)));

    /// <summary>The paths a logical line cites that do not resolve at the given milestone.</summary>
    private static IEnumerable<string> Unresolved(string line, string directory, int milestone) =>
        ClauseEdge.Split(line).SelectMany(clause => Backticked.Matches(clause).Select(m => m.Groups[1].Value).Where(IsCitation)
            .Concat(Link.Matches(clause).Select(m => Path.Join(directory, m.Groups[1].Value).Replace('\\', '/')))
            .Where(path => !Resolves(path, clause, milestone)));

    private static bool IsCitation(string span) =>
        (span.Contains('/', StringComparison.Ordinal) || Extensions.Any(e => span.EndsWith(e, StringComparison.Ordinal)))
        && !Elsewhere.Any(e => span.StartsWith(e, StringComparison.Ordinal));

    private static bool Resolves(string path, string clause, int milestone) =>
        Repository.Contains(path)
        || (!path.Contains('/', StringComparison.Ordinal) && FileNames.Value.Contains(path))
        || RunTime.Any(r => path.StartsWith(r, StringComparison.Ordinal))
        || (!IsNearMiss(path) && (
            Arrives.Matches(clause).Any(m => Documents.NotYetExited(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), milestone))
            || PlannedDirectories.Any(d => (path.TrimEnd('/') + "/").StartsWith(d.Path, StringComparison.Ordinal) && Documents.NotYetExited(d.Milestone, milestone))));

    /// <summary>
    /// A missing path one edit from an existing one is a misspelling, which no milestone excuses: a clause that says
    /// <c>at M0</c> of <c>.claude/hooks/</c> says nothing of <c>.claude/setings.json</c> beside it. The plan names no
    /// new path one edit from an old one.
    /// </summary>
    private static bool IsNearMiss(string path)
    {
        var missing = path.TrimEnd('/');
        return Repository.Entries.Concat(FileNames.Value).Any(e => OneEditApart(missing, e));
    }

    /// <summary>Whether two different strings differ by one character inserted, deleted or replaced, or two adjacent ones swapped.</summary>
    private static bool OneEditApart(string a, string b)
    {
        if (a == b || Math.Abs(a.Length - b.Length) > 1)
        {
            return false;
        }

        var same = 0;
        while (same < a.Length && same < b.Length && a[same] == b[same])
        {
            same++;
        }

        var (x, y) = (a[same..], b[same..]);
        return x.Length == y.Length
            ? x[1..] == y[1..] || (x.Length > 1 && x[0] == y[1] && x[1] == y[0] && x[2..] == y[2..])
            : x.Length > y.Length ? x[1..] == y : y[1..] == x;
    }

    /// <summary>The name of every file outside archive/, so a bare name such as <c>packages.lock.json</c> resolves.</summary>
    private static readonly Lazy<HashSet<string>> FileNames = new(() => Repository.Files
        .Where(f => !f.StartsWith("archive/", StringComparison.Ordinal)).Select(f => f.Split('/')[^1]).ToHashSet(StringComparer.Ordinal));
}
