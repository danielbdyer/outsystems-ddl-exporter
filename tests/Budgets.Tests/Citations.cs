using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// Every path a document of the tool cites resolves. The rule, as small as it can be and still honest:
/// <list type="bullet">
/// <item>A citation is a relative markdown link, or a backticked span of one word of path characters that holds a '/'
/// or ends in an extension this repository's files carry. Only the spans named in <see cref="Elsewhere"/> are skipped:
/// another repository's paths (the SSDT repository's pipeline checks its own), a branch, and a C# construct. So a misspelt root file
/// or directory (<c>DECISION.md</c>, <c>kernal/</c>) is a citation, and it fails.</item>
/// <item>It resolves when the file or directory exists, a bare file name when some file outside archive/ carries that
/// name; when it lies under a run-time root or is a file a verb writes at run time, which git never holds; when its
/// own clause says the milestone that creates it (<c>pending M3</c>, <c>from M7</c>, <c>at M8</c>, <c>not held yet: WP 3.4</c>,
/// <c>not held yet: the cutover tools</c>) and that milestone is not complete (<see cref="Documents.Complete(int)"/>: an exit no test
/// declares and no NEXT.md line names); or when it lies under a directory the plan creates, until that milestone is complete.</item>
/// <item>A clause ends at a ';', at a sentence's end and at a table cell's edge, so a milestone elsewhere on the line
/// excuses nothing. <c>until M8</c> and <c>M0's exit</c> never excuse a path: neither says the path arrives. Nor does
/// any milestone excuse a missing path one edit from an existing one, which is a misspelling.</item>
/// </list>
/// The root design documents are excluded until M8 starts; knowledge/ joins at M7.
/// Each document also cites in v3's forms, so a file WP 7.2 moves from v2's skills passes only once its citations are rewritten
/// (<see cref="Forms"/>): a finding by its identifier, never by v2's file; a sample by its shape under knowledge/samples/, never by
/// v2's sample-prs/; a handbook chapter by its title, never by its number or file name; a ledger by knowledge/ledgers/ or
/// dbchange/ledgers/, never by v2's estate/ or a bare name. A backticked span starting archive/ is provenance and exempt. Every F&lt;n&gt;
/// names a finding of knowledge/findings.md, or of v2's FINDINGS_AND_CHANGES.md until that file lands; and every
/// <c>dbchange &lt;verb&gt;</c> a document under knowledge/ or ARCHITECTURE.md cites is a row of the verb table, built or not.
/// </summary>
public sealed class Citations
{
    /// <summary>The extensions this repository's files carry; a bare name ending in one is a file the tree should hold.</summary>
    private static readonly string[] Extensions = [".md", ".json", ".cs", ".csproj", ".props", ".sh", ".txt", ".sln", ".allow"];

    /// <summary>
    /// Path-shaped spans that are not this repository's: the SSDT repository's files (<c>dbchange/…</c> and
    /// <c>tools/dbchange/</c>), the backport agent's <c>STATE.md</c>, a branch (<c>claude/…</c>, <c>wp/…</c>), and
    /// <c>try/finally</c>, which is C#. Each is a prefix; nothing else is skipped.
    /// </summary>
    private static readonly string[] Elsewhere = ["dbchange/", "tools/dbchange/", "STATE.md", "claude/", "wp/", "try/finally"];

    /// <summary>Where the tool writes at run time: git ignores the roots, and a verb writes the files wherever --out says.</summary>
    private static readonly string[] RunTime = [".dbchange/", "dist/", "gate.json", "changelog.json"];

    private static readonly (string Path, int Milestone)[] PlannedDirectories = [("knowledge/", 7), ("tests/Golden/", 0)];

    private static readonly Regex Backticked = new(@"`([\w.][\w.\-/]*)`", RegexOptions.CultureInvariant);

    private static readonly Regex Link = new(@"\]\((?![a-z]+:)([^)\s#]+)", RegexOptions.CultureInvariant);

    /// <summary>A clause's edge outside backticks: a ';', a table cell's '|', or the space after a sentence's end.</summary>
    private static readonly Regex ClauseEdge = new(@"(?:;|\||(?<=[.!?])\s)(?=(?:[^`]*`[^`]*`)*[^`]*$)", RegexOptions.CultureInvariant);

    /// <summary>
    /// A milestone that creates what its clause cites: pending M3, from M7, at M8, not held yet: WP 3.4; never M0's exit.
    /// </summary>
    private static readonly Regex Arrives =
        new(@"\b(?:(?:[Pp]ending|[Ff]rom|[Aa]t) M(\d+)(?!['’]s\b)|not held yet: WP (\d)\.\d+)", RegexOptions.CultureInvariant);

    /// <summary>The cutover tools, which arrive as M8 starts, if the operator decides to build them.</summary>
    private static readonly Regex CutoverTools = new(@"not held yet: the cutover tools", RegexOptions.CultureInvariant);

    public static TheoryData<string> ToolDocuments => new(Documents.Tool);

    /// <summary>v2's four citation forms, each with what it cites and the form v3 cites it in.</summary>
    private static readonly (Regex Pattern, string What, string Instead)[] Forms =
    [
        (new(@"(?<!archive/v2/ssdt-agent/)\bFINDINGS_AND_CHANGES\.md", RegexOptions.CultureInvariant),
            "a finding by its file", "cite the finding by its identifier, F<n>"),
        (new(@"(?<![\w/])(?:\.\./)*sample-prs/", RegexOptions.CultureInvariant),
            "a sample by its v2 path", "cite knowledge/samples/<shape>.md"),
        (new(@"handbook(?:'s)?\s+(?:file\s+)?\**\d+|\(=\s*§\d+|(?<![\w/])\d{2}-[A-Z][\w-]+\.md", RegexOptions.CultureInvariant),
            "the handbook by number", "cite the chapter by its title"),
        (new(@"(?<![\w/.-])(?:(?:\.\./)*(?:ssdt-agent/)?estate/|ledgers/)?"
            + @"(?:in-flight|operations|refusals|reviewers|row-tiers|scale-datapoints|toolchain|cdc-tracked)\.md", RegexOptions.CultureInvariant),
            "a ledger by its v2 or bare path",
            "cite knowledge/ledgers/<name> for the template the tool seeds, or dbchange/ledgers/<name> for the SSDT repository's rows"),
    ];

    /// <summary>A backticked span starting archive/: provenance, which cites v2 by its own paths.</summary>
    private static readonly Regex Provenance = new(@"`archive/[^`]*`", RegexOptions.CultureInvariant);

    private static readonly Regex FindingCited = new(@"\bF(\d+)\b", RegexOptions.CultureInvariant);

    private static readonly Regex VerbCited = new(@"`dbchange ([a-z][\w-]*)", RegexOptions.CultureInvariant);

    /// <summary>
    /// The findings: the <c>## F&lt;n&gt; · </c> headings of knowledge/findings.md, struck through or not, once it exists; until then
    /// the <c>- **F&lt;n&gt; — </c> items of v2's FINDINGS_AND_CHANGES.md.
    /// </summary>
    private static readonly Lazy<HashSet<int>> Findings = new(() => (Repository.Contains("knowledge/findings.md")
            ? Repository.Lines("knowledge/findings.md").Select(l => Regex.Match(l, @"^## ~*F(\d+) ·", RegexOptions.CultureInvariant))
            : Repository.Lines("archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md")
                .Select(l => Regex.Match(l, @"^- \*\*F(\d+) —", RegexOptions.CultureInvariant)))
        .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet());

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(ToolDocuments))]
    public void Every_document_of_the_repository_cites_a_finding_by_identifier_a_sample_by_shape_the_handbook_by_title_and_a_ledger_by_its_path(
        string document)
    {
        var verbs = document == "ARCHITECTURE.md" || document.StartsWith("knowledge/", StringComparison.Ordinal);
        var refused = Documents.Blocks(document).SelectMany(b => InV2Form(b.Text)
            .Concat(UnknownFindings(b.Text, Findings.Value))
            .Concat(verbs ? UnknownVerbs(b.Text) : [])
            .Select(r => document + ":" + b.Line.ToString(CultureInfo.InvariantCulture) + ": " + r));

        Assert.Empty(refused);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`FINDINGS_AND_CHANGES.md` F10.", "a finding by its file")]
    [InlineData("proven live in `../../../sample-prs/make-mandatory.md`", "a sample by its v2 path")]
    [InlineData("**A rename with no refactorlog entry** (handbook 16 = §19.1)", "the handbook by number")]
    [InlineData("Cite by **filename**: **08-Referential-Integrity-Basics.md**", "the handbook by number")]
    [InlineData("`dbchange check inflight` reads `estate/in-flight.md`", "a ledger by its v2 or bare path")]
    [InlineData("records in `ledgers/scale-datapoints.md`", "a ledger by its v2 or bare path")]
    [InlineData("reads `dbchange/ledgers/in-flight.md`", "")]
    [InlineData("`knowledge/ledgers/cdc-tracked.md` opens empty", "")]
    [InlineData("`archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md:140`", "")]
    [InlineData("`archive/v2/ssdt-agent/sample-prs/make-mandatory.md` is the v2 sample", "")]
    [InlineData("the handbook's chapter on the refactorlog", "")]
    [InlineData("outside the handbook no document shows", "")]
    public void A_citation_in_v2_s_form_is_refused_and_named_with_the_form_v3_cites(string line, string what) =>
        Assert.Equal(what, string.Join(", ", Forms.Where(f => f.Pattern.IsMatch(Provenance.Replace(line, ""))).Select(f => f.What)));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("F9 overturned F5", true)]
    [InlineData("F20, the scale lane's first light", true)]
    [InlineData("F21", false)]
    [InlineData("F0", false)]
    public void A_finding_cited_by_identifier_resolves_in_the_findings(string line, bool resolves) =>
        Assert.Equal(resolves, !UnknownFindings(line, Enumerable.Range(1, 20).ToHashSet()).Any());

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`dbchange prove`", true)]
    [InlineData("`dbchange check drift --target env:dev`", true)]
    [InlineData("`dbchange synthetic-copy up`", true)]
    [InlineData("`dbchange measure --tables`", false)]
    public void A_verb_a_knowledge_document_cites_is_a_row_of_the_verb_table(string line, bool resolves) =>
        Assert.Equal(resolves, !UnknownVerbs(line).Any());

    /// <summary>Each v2 form a logical line cites in, outside provenance, as the reason it is refused.</summary>
    private static IEnumerable<string> InV2Form(string line)
    {
        var cited = Provenance.Replace(line, "");
        return Forms.SelectMany(f => f.Pattern.Matches(cited).Select(m => "'" + m.Value + "' cites " + f.What + "; " + f.Instead));
    }

    private static IEnumerable<string> UnknownFindings(string line, IReadOnlySet<int> findings) =>
        FindingCited.Matches(Provenance.Replace(line, ""))
            .Where(m => !findings.Contains(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)))
            .Select(m => m.Value + " is no finding of " + (Repository.Contains("knowledge/findings.md") ? "knowledge/findings.md"
                : "archive/v2/ssdt-agent/FINDINGS_AND_CHANGES.md") + "; cite one that exists, or record it first");

    private static IEnumerable<string> UnknownVerbs(string line) =>
        VerbCited.Matches(line).Where(m => !DbChange.Cli.Contract.Verbs.Any(v => v.Name == m.Groups[1].Value))
            .Select(m => "'dbchange " + m.Groups[1].Value + "' is no row of the verb table (dbchange --help --json); cite the verb that exists");

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(ToolDocuments))]
    public void Every_path_a_document_of_the_repository_cites_exists_or_names_the_milestone_that_creates_it(string document)
    {
        var directory = Path.GetDirectoryName(document)!.Replace('\\', '/');
        var unresolved = Documents.Blocks(document).SelectMany(b => Unresolved(b.Text, directory, Documents.Complete)
            .Select(p => document + ":" + b.Line.ToString(CultureInfo.InvariantCulture) + ": " + p
                + " does not exist; fix the path, or say in its clause the milestone that creates it"));

        Assert.Empty(unresolved);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("kernel/BannedSymbols.txt", true)]
    [InlineData("kernal/", true)]
    [InlineData("DECISION.md", true)]
    [InlineData("LAWS.md", true)]
    [InlineData("packages.lock.json", true)]
    [InlineData(".dbchange/copies.json", true)]
    [InlineData("knowledge/description.md", true)]
    [InlineData("dbchange/ledgers/in-flight.md", false)]
    [InlineData("tools/dbchange/", false)]
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

    /// <summary>
    /// Each row names the milestones complete: every one below <paramref name="milestone"/>, as the build stands when M&lt;milestone&gt; is
    /// in progress.
    /// </summary>
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
    [InlineData("| D2 | … | `CultureInfo.CurrentCulture` is in `kernel/BannedSymbols.txt`; law 1′ runs under `tr-TR` | "
        + "`BannedSymbolsTests`; `Kernel.Tests: \"law 1′ under tr-TR\"` — not held yet: WP 3.3 |", 0, "")]
    [InlineData("| D2 | … | `CultureInfo.CurrentCulture` is in `kernel/BannedSymbol.txt`; law 1′ runs under `tr-TR` | "
        + "`BannedSymbolsTests`; `Kernel.Tests: \"law 1′ under tr-TR\"` — not held yet: WP 3.3 |", 0, "kernel/BannedSymbol.txt")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs` over both | `Io.Tests` on both local servers — not held yet: WP 3.4 |", 0,
        "io/SyntheticCopy.cs")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs` over both, from M3 | `Io.Tests` on both local servers — not held yet: WP 3.4 |", 0, "")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs` over both, from M3 | `Io.Tests` on both local servers — not held yet: WP 3.4 |", 4,
        "io/SyntheticCopy.cs")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs`, not held yet: WP 3.4 | … |", 0, "")]
    [InlineData("| O1 | … | `io/SyntheticCopy.cs`, not held yet: WP 3.4 | … |", 4, "io/SyntheticCopy.cs")]
    [InlineData("| E4 | … | `io/Emit.cs`, not held yet: the cutover tools | … |", 7, "")]
    [InlineData("| E4 | … | `io/Emit.cs`, not held yet: the cutover tools | … |", 8, "io/Emit.cs")]
    [InlineData("record the trust state of each constraint under `tests/Golden/trust-states/`", 0, "")]
    [InlineData("record the trust state of each constraint under `tests/Golden/trust-states/`", 1, "tests/Golden/trust-states/")]
    [InlineData("builds `cli` into `.dbchange/bin/` when stale; no `gate.json` carries a password", 0, "")]
    [InlineData("`packages.lock.json` in every project", 0, "")]
    public void A_missing_path_resolves_only_through_a_milestone_its_own_clause_names(string line, int milestone, string unresolved) =>
        Assert.Equal(unresolved, string.Join(", ", Unresolved(line, "", n => n < milestone)));

    /// <summary>
    /// The paths a logical line cites that do not resolve, under <paramref name="complete"/> saying which milestones are.
    /// </summary>
    private static IEnumerable<string> Unresolved(string line, string directory, Func<int, bool> complete) =>
        ClauseEdge.Split(line).SelectMany(clause => Backticked.Matches(clause).Select(m => m.Groups[1].Value).Where(IsCitation)
            .Concat(Link.Matches(clause).Select(m => Path.Join(directory, m.Groups[1].Value).Replace('\\', '/')))
            .Where(path => !Resolves(path, clause, complete)));

    private static bool IsCitation(string span) =>
        (span.Contains('/', StringComparison.Ordinal) || Extensions.Any(e => span.EndsWith(e, StringComparison.Ordinal)))
        && !Elsewhere.Any(e => span.StartsWith(e, StringComparison.Ordinal));

    private static bool Resolves(string path, string clause, Func<int, bool> complete) =>
        Repository.Contains(path)
        || (!path.Contains('/', StringComparison.Ordinal) && FileNames.Value.Contains(path))
        || RunTime.Any(r => path.StartsWith(r, StringComparison.Ordinal))
        || (!IsNearMiss(path) && (
            Arrives.Matches(clause)
                .Any(m => !complete(int.Parse(m.Groups[m.Groups[1].Success ? 1 : 2].Value, CultureInfo.InvariantCulture)))
            || (CutoverTools.IsMatch(clause) && !Documents.Started(Documents.OneTool, complete))
            || PlannedDirectories.Any(d => (path.TrimEnd('/') + "/").StartsWith(d.Path, StringComparison.Ordinal)
                && !complete(d.Milestone))));

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

    /// <summary>
    /// Whether two different strings differ by one character inserted, deleted or replaced, or two adjacent ones swapped.
    /// </summary>
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
