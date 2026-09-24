using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// A decision is one line: its date, the decision in one sentence, and the pull request that carries its reasoning,
/// as <c>2026-09-23 · the decision · #704</c>. DECISIONS.md is its one heading and those lines, nothing else.
/// </summary>
public sealed class Decisions
{
    private static readonly Regex Line = new(@"^\d{4}-\d{2}-\d{2} · .+ · #\d+$", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "fast")]
    public void Every_line_under_the_heading_is_one_dated_decision_with_its_pull_request()
    {
        var lines = Repository.Lines("DECISIONS.md");

        Assert.StartsWith("# ", lines[0], StringComparison.Ordinal);
        Assert.Empty(lines.Select((line, i) => (line, i)).Skip(1).Where(x => !IsDecision(x.line))
            .Select(x => "DECISIONS.md:" + (x.i + 1).ToString(CultureInfo.InvariantCulture) + " is not <yyyy-mm-dd> · <the decision> · #<pull request>: " + x.line));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("2026-09-23 · v3 is C# · #703", true)]
    [InlineData("2026-09-23 · v3 is C#", false)]
    [InlineData("2026-09-23 - v3 is C# - #703", false)]
    [InlineData("2026-02-30 · v3 is C# · #703", false)]
    [InlineData("", false)]
    [InlineData("## Status", false)]
    public void The_rule_takes_a_decision_line_and_nothing_else(string line, bool decision) => Assert.Equal(decision, IsDecision(line));

    private static bool IsDecision(string line) =>
        Line.IsMatch(line) && DateOnly.TryParseExact(line[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
