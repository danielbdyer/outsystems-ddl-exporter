using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Cli;
using Xunit;
using Xunit.Abstractions;

namespace DbChange.Budgets.Tests;

/// <summary>
/// V3_PARITY.md's ledger holds, row by row, what v2 does, where v2 proves it, and where v3 holds it (DECISIONS.md, 2026-10-19).
/// Every backticked path the v2 cell names exists in the repository, and a line it names (<c>path:123</c>) exists in that file, so a
/// row cites v2's proof as the archive holds it. Each clause of the v3 cell (clauses split at ';' outside backticks) names a test that
/// exists (a class, or <c>Io.Tests: "its English name"</c>), or a law, a value or an exit some test declares (<c>law 2′</c>,
/// <c>value S7</c>, <c>exit M1.3</c>), with every file and verb it names existing; or says <c>not held yet: WP &lt;n&gt;.&lt;m&gt;</c>,
/// accepted while M&lt;n&gt; is not complete, or <c>not held yet: the cutover tools</c>, accepted until M8 starts; or begins
/// <c>retired:</c> and names the file whose case shows no verdict depends on it. When a milestone completes, each row still waiting on
/// one of its work packages fails until it names the test that holds it.
/// </summary>
public sealed class ParityResolves(ITestOutputHelper output)
{
    private const string Ledger = "V3_PARITY.md";

    private static readonly Regex Row = new(@"^\|\s*(PAR-\d{3})\s*\|", RegexOptions.CultureInvariant);
    private static readonly Regex Clause = new(@";\s+(?=(?:[^`]*`[^`]*`)*[^`]*$)", RegexOptions.CultureInvariant);
    private static readonly Regex NotHeldYet = new(@"— not held yet: (?:WP (\d)\.(\d+)|the cutover tools)$", RegexOptions.CultureInvariant);
    private static readonly Regex Name = new("`([^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex Test = new(@"^(\w+\.Tests): (?:""(.+)""|([\w.]+))$", RegexOptions.CultureInvariant);
    private static readonly Regex Law = new(@"\blaw (\d+′?|[A-Z])(?=[\s,;.)]|$)", RegexOptions.CultureInvariant);
    private static readonly Regex Value = new(@"\bvalue ([A-Z]\d+)\b", RegexOptions.CultureInvariant);
    private static readonly Regex Exit = new(@"\bexit (M\d\.\d+)\b", RegexOptions.CultureInvariant);
    private static readonly Regex Line = new(@"^(.+?):(\d+)$", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "fast")]
    public void Every_row_of_the_parity_ledger_names_where_v2_proves_it_and_where_v3_holds_it()
    {
        var rows = Repository.Lines(Ledger).Where(l => Row.IsMatch(l)).Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .ToList();
        var states = rows.Select(r => (Id: r[0], State: State(r[^1]))).ToList();
        output.WriteLine(Ledger + ": " + string.Join(", ", states.GroupBy(s => s.State).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key + " " + g.Count().ToString(CultureInfo.InvariantCulture))));

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(5, r.Length));
        Assert.Equal(rows.Count, rows.Select(r => r[0]).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(rows.SelectMany(r =>
            UnresolvedProof(r[2]).Concat(Clause.Split(r[^1]).SelectMany(c => Unresolved(c, Documents.Complete)))
            .Select(p => Ledger + " " + r[0] + ": " + p)));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`Io.Tests: SpikeTests`; law 3′", true)]
    [InlineData("law 2′ and exit M1.3", true)]
    [InlineData("value S7", true)]
    [InlineData("law 99", false)]
    [InlineData("exit M1.99", false)]
    [InlineData("`Io.Tests: \"no test has these words\"`", false)]
    [InlineData("the corpus, prose with no test", false)]
    [InlineData("`dbchange check` and exit M1.3", true)]
    [InlineData("`dbchange check` alone; exit M1.3", false)]
    [InlineData("`dbchange prove` and law 2′", false)]
    [InlineData("`.github/workflows/dbchange.yml` alone", false)]
    [InlineData("`kernel/Witness.cs` — not held yet: WP 3.8", true)]
    [InlineData("the planter — not held yet: WP 3.9", false)]
    [InlineData("`Io.Tests: SpikeTests` — not held yet: WP 4.4", false)]
    [InlineData("emit's bytes — not held yet: the cutover tools", true)]
    [InlineData("retired: no verdict depends on it, `archive/v2/TWIN_SUCCESSION.md`", true)]
    [InlineData("retired: no verdict depends on it", false)]
    public void A_clause_resolves_when_it_names_a_test_a_law_a_value_or_an_exit_that_exists_or_the_work_package_still_to_come(string clause,
        bool resolves) =>
        Assert.Equal(resolves, !Clause.Split(clause).SelectMany(c => Unresolved(c, _ => false)).Any());

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`archive/v2/TWIN_SUCCESSION.md`", true)]
    [InlineData("`archive/v2/TWIN_SUCCESSION.md:1`", true)]
    [InlineData("`archive/v2/TWIN_SUCCESSION.md:999999`", false)]
    [InlineData("`archive/v2/NO_SUCH_FILE.md`", false)]
    [InlineData("the proof lane, with no path", false)]
    public void A_v2_proof_resolves_when_each_path_it_names_exists_and_holds_the_line_it_names(string cell, bool resolves) =>
        Assert.Equal(resolves, !UnresolvedProof(cell).Any());

    /// <summary>A row's state, by its v3 cell: held, partly held, not held yet, the cutover tools, or retired.</summary>
    private static string State(string cell)
    {
        var clauses = Clause.Split(cell);
        var waiting = clauses.Count(c => NotHeldYet.IsMatch(c));
        return clauses.Any(c => c.Contains("the cutover tools", StringComparison.Ordinal)) ? "the cutover tools"
            : clauses.Any(c => c.StartsWith("retired:", StringComparison.Ordinal)) ? "retired"
            : waiting == 0 ? "held"
            : waiting < clauses.Length ? "partly held"
            : "not held yet";
    }

    /// <summary>What the v2 cell fails to resolve: a backticked path that is no file or directory, or a line past its file's end.</summary>
    private static IEnumerable<string> UnresolvedProof(string cell)
    {
        var names = Name.Matches(cell).Select(m => m.Groups[1].Value).ToList();
        if (names.Count == 0)
        {
            yield return "the v2 cell names no file that proves it";
        }

        foreach (var name in names)
        {
            var (path, line) = Line.Match(name) is { Success: true } m
                ? (m.Groups[1].Value, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) : (name, 0);
            if (!Repository.Contains(path))
            {
                yield return "`" + path + "` names no file or directory";
            }
            else if (line > 0 && line > Repository.PhysicalLines(path))
            {
                yield return "`" + name + "` names a line past the end of " + path;
            }
        }
    }

    /// <summary>
    /// What a clause of the v3 cell fails to resolve, as the reason, under <paramref name="complete"/> saying which milestones are; nothing
    /// when it resolves.
    /// </summary>
    private static IEnumerable<string> Unresolved(string clause, Func<int, bool> complete)
    {
        var names = Name.Matches(clause).Select(m => m.Groups[1].Value).ToList();
        if (clause.StartsWith("retired:", StringComparison.Ordinal))
        {
            if (!names.Any(Repository.Contains))
            {
                yield return "'" + clause + "' retires a v2 behavior without naming the file whose case shows no verdict depends on it";
            }

            yield break;
        }

        if (NotHeldYet.Match(clause) is { Success: true } later)
        {
            var (accepted, what) = later.Groups[1].Success
                ? (Documents.WorkPackages.Contains((Number(later, 1), Number(later, 2))) && !complete(Number(later, 1)),
                    "WP " + later.Groups[1].Value + "." + later.Groups[2].Value)
                : (!Documents.Started(Documents.OneTool, complete), "the cutover tools");
            if (!accepted)
            {
                yield return "'" + clause + "' waits on " + what
                    + ", which the plan does not list or whose milestone is complete; name the test that holds it";
            }

            foreach (var built in names.Where(n => IsTestName(n) && Tests(n).Any()))
            {
                yield return "`" + built + "` exists; drop its 'not held yet'";
            }

            yield break;
        }

        var traits = Law.Matches(clause).Select(m => ("Law", m.Groups[1].Value))
            .Concat(Value.Matches(clause).Select(m => ("Value", m.Groups[1].Value)))
            .Concat(Exit.Matches(clause).Select(m => ("Exit", m.Groups[1].Value))).ToList();
        if (traits.Count == 0 && !names.Any(IsTestName))
        {
            yield return "'" + clause + "' names no test, law, value or exit that holds it";
        }

        foreach (var (trait, id) in traits.Where(t => !Declared(t.Item1, t.Item2)))
        {
            yield return "no test declares " + trait.ToLowerInvariant() + " " + id;
        }

        foreach (var name in names.Where(n => !Resolves(n)))
        {
            yield return "`" + name + "` names no test, file, directory or verb this build has";
        }
    }

    /// <summary>
    /// Whether a test declares the trait: a law by its first word (2′, P), a value by its row, an exit by its milestone and number.
    /// </summary>
    private static bool Declared(string trait, string id) => TestTraits.All.Any(t => t.Values(trait)
        .Any(v => trait == "Law" ? v.Split(' ')[0] == id : v == id));

    /// <summary>Whether a backticked name picks out a test, a file or directory, or a verb this build has.</summary>
    private static bool Resolves(string name) =>
        IsTestName(name) ? Tests(name).Any()
        : name.StartsWith("dbchange ", StringComparison.Ordinal)
            ? Contract.Verbs.Any(v => v.Name == name["dbchange ".Length..].Split(' ')[0] && v.Built)
        : Repository.Contains(name);

    /// <summary>A project and a quoted English name or a class (<c>Io.Tests: DriftTests</c>), or a bare class name.</summary>
    private static bool IsTestName(string name) => Test.IsMatch(name) || Regex.IsMatch(name, @"^\w+$", RegexOptions.CultureInvariant);

    /// <summary>The tests a name picks out: by project and words for a quoted name, by the class's last name otherwise.</summary>
    private static IEnumerable<TestTraits.Test> Tests(string name)
    {
        var test = Test.Match(name);
        return test.Success
            ? TestTraits.All.Where(t => t.Project == test.Groups[1].Value
                && (test.Groups[2].Success ? t.Words == TestTraits.Words(test.Groups[2].Value)
                    : ("." + t.Class).EndsWith("." + test.Groups[3].Value, StringComparison.Ordinal)))
            : TestTraits.All.Where(t => t.Class.EndsWith("." + name, StringComparison.Ordinal));
    }

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
