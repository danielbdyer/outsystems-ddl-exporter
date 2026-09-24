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
/// Every value in VALUES.md names where its mechanism lives. Each clause of a Where (clauses split at ';') either
/// names things that exist, or says <c>pending M&lt;n&gt;</c>, accepted through M&lt;n&gt; and refused once its exit
/// raises <see cref="Contract.Milestone"/> past n, or <c>pending W</c>, accepted until M8 starts, or says
/// <c>prose only</c>. <see cref="Documents.NotYetExited"/> and <see cref="Documents.Before"/> hold the two boundaries,
/// and the minimal pairs below pin each at the milestone it ends on. A name is a backticked test
/// (<c>Budgets.Tests: Manifest</c>, <c>Kernel.Tests: "an English name"</c>, or a test class), a file or directory, or
/// <c>estate &lt;verb&gt;</c> once the verb is built. A pending clause whose test already exists drops its
/// <c>pending</c>. The rows that say <c>prose only</c> are printed, and their number rises only with a DECISIONS.md line.
/// </summary>
public sealed class ValuesResolve(ITestOutputHelper output)
{
    private const int ProseOnlyCeiling = 4;

    private static readonly Regex Row = new(@"^\|\s*([A-Z]\d+)\s*\|", RegexOptions.CultureInvariant);
    private static readonly Regex Clause = new(@";\s+(?=(?:[^`]*`[^`]*`)*[^`]*$)", RegexOptions.CultureInvariant);
    private static readonly Regex Pending = new(@"— pending (?:M(\d+)|W)$", RegexOptions.CultureInvariant);
    private static readonly Regex Name = new("`([^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex Test = new(@"^(\w+\.Tests): (?:""(.+)""|([\w.]+))$", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "fast")]
    public void Every_value_names_where_it_is_held()
    {
        var rows = Repository.Lines("VALUES.md").Where(l => Row.IsMatch(l)).Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray()).ToList();
        var proseOnly = rows.Where(r => r[^1].Contains("prose only", StringComparison.Ordinal)).Select(r => r[0]).ToList();
        output.WriteLine("VALUES.md rows held by prose only: " + proseOnly.Count.ToString(CultureInfo.InvariantCulture) + " (" + string.Join(", ", proseOnly) + ")");

        Assert.All(rows, r => Assert.Equal(5, r.Length));
        Assert.Empty(rows.SelectMany(r => Clause.Split(r[^1]).SelectMany(c => Unresolved(c, Documents.Milestone)).Select(p => "VALUES.md " + r[0] + ": " + p)));
        Assert.True(proseOnly.Count <= ProseOnlyCeiling, "more VALUES.md rows are held by prose only than the ceiling of " + ProseOnlyCeiling.ToString(CultureInfo.InvariantCulture) + "; add a mechanism, or raise the ceiling with a DECISIONS.md line");
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`Budgets.Tests: Manifest`", true)]
    [InlineData("`Budgets.Tests: Register.Prose`", true)]
    [InlineData("`BannedSymbolsTests`; `Directory.Build.props`", true)]
    [InlineData("`Budgets.Tests: NoSuchLaw`", false)]
    [InlineData("the dependency laws in `Budgets.Tests`", false)]
    [InlineData("the outbound-deny job", false)]
    [InlineData("`Kernel.Tests: \"no arm returns success by default\"` — pending M1", true)]
    [InlineData("`Budgets.Tests: Manifest` — pending M1", false)]
    [InlineData("`estate doctor`", false)]
    [InlineData("the author rule, prose only", true)]
    public void A_clause_resolves_when_it_names_what_exists_or_a_milestone_not_yet_exited(string clause, bool resolves) =>
        Assert.Equal(resolves, !Clause.Split(clause).SelectMany(c => Unresolved(c, Documents.Milestone)).Any());

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("pending M0", 0, true)]
    [InlineData("pending M0", 1, false)]
    [InlineData("pending M3", 3, true)]
    [InlineData("pending M3", 4, false)]
    [InlineData("pending W", 7, true)]
    [InlineData("pending W", 8, false)]
    public void A_pending_clause_is_refused_once_its_milestone_has_passed(string pending, int milestone, bool resolves) =>
        Assert.Equal(resolves, !Unresolved("`Kernel.Tests: \"no arm returns success by default\"` — " + pending, milestone).Any());

    /// <summary>What a clause fails to resolve while <paramref name="milestone"/> is in progress, as the reason; nothing when it resolves.</summary>
    private static IEnumerable<string> Unresolved(string clause, int milestone)
    {
        var pending = Pending.Match(clause);
        var names = Name.Matches(clause).Select(m => m.Groups[1].Value).ToList();
        if (pending.Success)
        {
            var accepted = pending.Groups[1].Success
                ? Documents.NotYetExited(int.Parse(pending.Groups[1].Value, CultureInfo.InvariantCulture), milestone)
                : Documents.Before(Documents.OneTool, milestone);
            if (!accepted)
            {
                yield return "'" + clause + "' is past its milestone; name the test that holds it";
            }

            foreach (var built in names.Where(n => Test.IsMatch(n) && Exists(n, milestone)))
            {
                yield return "`" + built + "` exists; drop its 'pending'";
            }
        }
        else if (!clause.Contains("prose only", StringComparison.Ordinal))
        {
            if (names.Count == 0)
            {
                yield return "'" + clause + "' names no test, file or verb";
            }

            foreach (var missing in names.Where(n => !Exists(n, milestone)))
            {
                yield return "`" + missing + "` names no test, file or built verb";
            }
        }
    }

    private static bool Exists(string name, int milestone)
    {
        var test = Test.Match(name);
        if (test.Success)
        {
            var declared = Declared.Value.Where(d => d.Project == test.Groups[1].Value);
            return test.Groups[2].Success
                ? declared.Any(d => d.Words == Words(test.Groups[2].Value))
                : declared.Any(d => d.Words.Length == 0 && ("." + d.Name).EndsWith("." + test.Groups[3].Value, StringComparison.Ordinal));
        }

        if (name.StartsWith("estate ", StringComparison.Ordinal))
        {
            return Contract.Verbs.Any(v => v.Name == name[7..] && v.Body is not null && v.Arrives <= milestone);
        }

        return Regex.IsMatch(name, @"^\w+$", RegexOptions.CultureInvariant)
            ? Declared.Value.Any(d => d.Words.Length == 0 && d.Name.EndsWith("." + name, StringComparison.Ordinal))
            : !name.EndsWith(".Tests", StringComparison.Ordinal) && Repository.Contains(name);
    }

    /// <summary>Every class and every test method declared under tests/, by project: a class by its full name, a method by its words.</summary>
    private static readonly Lazy<List<(string Project, string Name, string Words)>> Declared = new(() => Repository.Files
        .Where(f => f.StartsWith("tests/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal))
        .SelectMany(f =>
        {
            var text = Repository.Read(f);
            var project = f.Split('/')[1];
            var space = Regex.Match(text, @"^namespace ([\w.]+);", RegexOptions.Multiline | RegexOptions.CultureInvariant).Groups[1].Value;
            return Regex.Matches(text, @"\bclass (\w+)", RegexOptions.CultureInvariant).Select(m => (project, space + "." + m.Groups[1].Value, ""))
                .Concat(Regex.Matches(text, @"public (?:async )?\w+ (\w+)\(", RegexOptions.CultureInvariant).Select(m => (project, m.Groups[1].Value, Words(m.Groups[1].Value))));
        })
        .ToList());

    /// <summary>A name as its words: lower case, letters and digits only, one space between, so a method's name and its English name compare.</summary>
    private static string Words(string name) => string.Join(' ', Regex.Split(name.ToLowerInvariant(), @"[^\p{Ll}\p{Nd}]+", RegexOptions.CultureInvariant).Where(w => w.Length > 0));
}
