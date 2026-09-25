using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Estate.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Estate.Budgets.Tests;

/// <summary>
/// Every row of VALUES.md names where it is held (DECISIONS.md, 2026-09-25). Each clause of a Where (clauses split at ';' outside
/// backticks) names a test that declares the row with [Trait("Value", "&lt;row&gt;")] (a class, or <c>Kernel.Tests: "its English name"</c>),
/// a file or directory, a verb this build has (<c>estate doctor</c>) or a job of .github/workflows/estate.yml; or says
/// <c>not held yet: WP &lt;n&gt;.&lt;m&gt;</c>, accepted while M&lt;n&gt; is not complete (<see cref="Documents.Complete(int)"/>), or
/// <c>not held yet: the cutover tools</c>, accepted until M8 starts; or says <c>prose only</c>, which is printed and capped. A test the
/// clause names that exists drops its <c>not held yet</c>. <c>pending M&lt;n&gt;</c> and <c>pending W</c> are retired phrases and fail.
/// Every Value trait names a row.
/// </summary>
public sealed class ValuesResolve(ITestOutputHelper output)
{
    private const int ProseOnlyCeiling = 4;

    private static readonly Regex Row = new(@"^\|\s*([A-Z]\d+)\s*\|", RegexOptions.CultureInvariant);
    private static readonly Regex Clause = new(@";\s+(?=(?:[^`]*`[^`]*`)*[^`]*$)", RegexOptions.CultureInvariant);
    private static readonly Regex NotHeldYet = new(@"— not held yet: (?:WP (\d)\.(\d+)|the cutover tools)$", RegexOptions.CultureInvariant);
    private static readonly Regex Retired = new(@"\bpending (?:M\d|W)\b", RegexOptions.CultureInvariant);
    private static readonly Regex Name = new("`([^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex Test = new(@"^(\w+\.Tests): (?:""(.+)""|([\w.]+))$", RegexOptions.CultureInvariant);
    private static readonly Regex Job = new(@"\bthe ([a-z][\w-]*) job\b", RegexOptions.CultureInvariant);
    private static readonly Regex JobId = new(@"^  ([a-z][\w-]*):$", RegexOptions.CultureInvariant);

    /// <summary>The jobs of the workflow, by id: fast, sql-ubuntu, sql-windows, sql-offline.</summary>
    private static readonly Lazy<HashSet<string>> Jobs = new(() => Repository.Lines(".github/workflows/estate.yml")
        .Select(l => JobId.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal));

    [Fact]
    [Trait("Category", "fast")]
    public void Every_value_names_where_it_is_held()
    {
        var rows = Repository.Lines("VALUES.md").Where(l => Row.IsMatch(l)).Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray()).ToList();
        var proseOnly = rows.Where(r => r[^1].Contains("prose only", StringComparison.Ordinal)).Select(r => r[0]).ToList();
        var declared = TestTraits.All.SelectMany(t => t.Values("Value")).ToHashSet(StringComparer.Ordinal);
        output.WriteLine("VALUES.md rows held by prose only: " + proseOnly.Count.ToString(CultureInfo.InvariantCulture) + " (" + string.Join(", ", proseOnly) + ")");
        output.WriteLine("VALUES.md rows no test declares: " + string.Join(", ", rows.Select(r => r[0]).Where(r => !declared.Contains(r))));

        Assert.All(rows, r => Assert.Equal(5, r.Length));
        Assert.Empty(rows.SelectMany(r => Clause.Split(r[^1]).SelectMany(c => Unresolved(r[0], c, Documents.Complete)).Select(p => "VALUES.md " + r[0] + ": " + p)));
        Assert.True(proseOnly.Count <= ProseOnlyCeiling, "more VALUES.md rows are held by prose only than the ceiling of " + ProseOnlyCeiling.ToString(CultureInfo.InvariantCulture) + "; add a mechanism, or raise the ceiling with a DECISIONS.md line");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_declared_value_names_a_row_of_VALUES_md()
    {
        var rows = Documents.Values.ToHashSet(StringComparer.Ordinal);

        Assert.Empty(TestTraits.All.SelectMany(t => t.Values("Value").Where(v => !rows.Contains(v)).Select(v => t.FullName + " declares Value " + v + ", which is no row of VALUES.md")));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("L4", "`Budgets.Tests: Manifest`", true)]
    [InlineData("L1", "`Budgets.Tests: Manifest`", false)]
    [InlineData("L4", "`Budgets.Tests: NoSuchTest`", false)]
    [InlineData("L4", "`Budgets.Tests: \"every markdown file outside the archive and every ci file is a row\"`", true)]
    [InlineData("L2", "`Budgets.Tests: \"every markdown file outside the archive and every ci file is a row\"`", false)]
    [InlineData("L7", "`BannedSymbolsTests`; `Directory.Build.props`", true)]
    [InlineData("L4", "the dependency laws in `Budgets.Tests`", false)]
    [InlineData("L4", "`estate doctor`", true)]
    [InlineData("L4", "`estate predict`", false)]
    [InlineData("A6", "`AGENTS.md`", true)]
    [InlineData("O10", "the sql-offline job of `.github/workflows/estate.yml`", true)]
    [InlineData("O10", "the sql-elsewhere job of `.github/workflows/estate.yml`", false)]
    [InlineData("A5", "the author rule, prose only", true)]
    [InlineData("L4", "`Budgets.Tests: Manifest` — pending M1", false)]
    [InlineData("D1", "law 1, its emit form — pending W", false)]
    public void A_clause_resolves_when_it_names_a_test_that_declares_the_row_or_a_file_a_built_verb_or_a_job_that_holds_it(string row, string clause, bool resolves) =>
        Assert.Equal(resolves, !Clause.Split(clause).SelectMany(c => Unresolved(row, c, Documents.Complete)).Any());

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`Kernel.Tests: \"classify says provisional first\"` — not held yet: WP 2.5", false, true)]
    [InlineData("`Kernel.Tests: \"classify says provisional first\"` — not held yet: WP 2.5", true, false)]
    [InlineData("law 1, its emit form — not held yet: the cutover tools", false, true)]
    [InlineData("law 1, its emit form — not held yet: the cutover tools", true, false)]
    [InlineData("`Budgets.Tests: Manifest` — not held yet: WP 2.5", false, false)]
    [InlineData("`Kernel.Tests: \"classify says provisional first\"` — not held yet: WP 2.9", false, false)]
    public void A_clause_not_held_yet_resolves_while_its_work_package_s_milestone_is_incomplete(string clause, bool complete, bool resolves) =>
        Assert.Equal(resolves, !Unresolved("A1", clause, _ => complete).Any());

    /// <summary>What a clause of <paramref name="row"/> fails to resolve, as the reason, under <paramref name="complete"/> saying which milestones are; nothing when it resolves.</summary>
    private static IEnumerable<string> Unresolved(string row, string clause, Func<int, bool> complete)
    {
        if (Retired.IsMatch(clause))
        {
            yield return "'" + clause + "' says pending, a retired phrase; write not held yet: WP <n>.<m>, or not held yet: the cutover tools";
            yield break;
        }

        var names = Name.Matches(clause).Select(m => m.Groups[1].Value).ToList();
        if (NotHeldYet.Match(clause) is { Success: true } later)
        {
            var (accepted, what) = later.Groups[1].Success
                ? (Documents.WorkPackages.Contains((Number(later, 1), Number(later, 2))) && !complete(Number(later, 1)), "WP " + later.Groups[1].Value + "." + later.Groups[2].Value)
                : (!Documents.Started(Documents.OneTool, complete), "the cutover tools");
            if (!accepted)
            {
                yield return "'" + clause + "' waits on " + what + ", which the plan does not list or whose milestone is complete; name the test that holds it";
            }

            foreach (var built in names.Where(n => IsTestName(n) && Tests(n).Any()))
            {
                yield return "`" + built + "` exists; drop its 'not held yet' and give it [Trait(\"Value\", \"" + row + "\")]";
            }
        }
        else if (!clause.Contains("prose only", StringComparison.Ordinal))
        {
            if (names.Count == 0 && !Job.IsMatch(clause))
            {
                yield return "'" + clause + "' names no test, file, verb or job";
            }

            foreach (var reason in names.SelectMany(n => Unresolved(row, n)))
            {
                yield return reason;
            }

            foreach (var job in Job.Matches(clause).Select(m => m.Groups[1].Value).Where(j => !Jobs.Value.Contains(j)))
            {
                yield return "the " + job + " job is no job of .github/workflows/estate.yml";
            }
        }
    }

    /// <summary>Why a backticked name resolves nothing for <paramref name="row"/>: a test that does not declare it, or a name that is no test, file or built verb.</summary>
    private static IEnumerable<string> Unresolved(string row, string name)
    {
        if (IsTestName(name))
        {
            var tests = Tests(name).ToList();
            if (tests.Count == 0)
            {
                yield return "`" + name + "` names no test";
            }
            else if (!tests.Any(t => t.Values("Value").Contains(row)))
            {
                yield return "`" + name + "` declares no [Trait(\"Value\", \"" + row + "\")]";
            }
        }
        else if (name.StartsWith("estate ", StringComparison.Ordinal))
        {
            if (!Contract.Verbs.Any(v => v.Name == name[7..].Split(' ')[0] && v.Built))
            {
                yield return "`" + name + "` is no verb this build has";
            }
        }
        else if (name.EndsWith(".Tests", StringComparison.Ordinal) || !Repository.Contains(name))
        {
            yield return "`" + name + "` names no file or directory";
        }
    }

    /// <summary>A project and a quoted English name or a class (<c>Io.Tests: DriftTests</c>), or a bare class name.</summary>
    private static bool IsTestName(string name) => Test.IsMatch(name) || Regex.IsMatch(name, @"^\w+$", RegexOptions.CultureInvariant);

    /// <summary>The tests a name picks out: by project and words for a quoted name, by the class's last name otherwise.</summary>
    private static IEnumerable<TestTraits.Test> Tests(string name)
    {
        var test = Test.Match(name);
        return test.Success
            ? TestTraits.All.Where(t => t.Project == test.Groups[1].Value
                && (test.Groups[2].Success ? t.Words == TestTraits.Words(test.Groups[2].Value) : ("." + t.Class).EndsWith("." + test.Groups[3].Value, StringComparison.Ordinal)))
            : TestTraits.All.Where(t => t.Class.EndsWith("." + name, StringComparison.Ordinal));
    }

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
