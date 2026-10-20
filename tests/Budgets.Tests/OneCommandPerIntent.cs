using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// Value O3: every step of a workflow is a dbchange verb, so no tool document outside knowledge/handbook/ shows a sqlpackage, sqlcmd
/// or docker invocation. An invocation is a backticked span that starts with the tool's name and a space, or a line inside a ```
/// fence that does. The tool's bare name in backticks, and Docker the product named in prose, are not invocations.
/// </summary>
public sealed class OneCommandPerIntent
{
    private const string Handbook = "knowledge/handbook/";

    private static readonly Regex InBackticks = new(@"`(?:sqlpackage|sqlcmd|docker)\s[^`]*`", RegexOptions.CultureInvariant);

    private static readonly Regex InFence = new(@"^\s*(?:sqlpackage|sqlcmd|docker)\s", RegexOptions.CultureInvariant);

    public static TheoryData<string> OutsideTheHandbook =>
        new(Documents.Tool.Where(d => !d.StartsWith(Handbook, StringComparison.Ordinal)));

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "O3")]
    [MemberData(nameof(OutsideTheHandbook))]
    public void No_document_outside_the_handbook_invokes_sqlpackage_sqlcmd_or_docker(string document)
    {
        var invocations = Invocations(Repository.Lines(document))
            .Select(i => document + ":" + i.Line.ToString(CultureInfo.InvariantCulture) + ": '" + i.Text
                + "' invokes a tool a dbchange verb stands in for; name the dbchange verb that does it");

        Assert.Empty(invocations);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`docker inspect dbchange-sql`", true)]
    [InlineData("`sqlpackage /Action:Script`", true)]
    [InlineData("and `sqlcmd -S <Dev's server> -E -Q \"SELECT 1\"`.", true)]
    [InlineData("`sqlpackage`, `sqlcmd` or `docker`", false)]
    [InlineData("SQL Server in Docker from the pinned image", false)]
    [InlineData("the docker image runs sqlcmd for the operator", false)]
    [InlineData("```\nsqlcmd -S . -Q \"SELECT 1\"\n```", true)]
    [InlineData("```\ndbchange check drift --target env:dev\n```", false)]
    [InlineData("sqlcmd -S . -Q \"SELECT 1\"", false)]
    public void An_invocation_is_a_backticked_or_fenced_command_line_starting_with_the_tool(string text, bool invokes) =>
        Assert.Equal(invokes, Invocations(text.Split('\n')).Any());

    /// <summary>Each invocation in a document's physical lines, with the line it stands on.</summary>
    private static IEnumerable<(int Line, string Text)> Invocations(IReadOnlyList<string> lines)
    {
        var fenced = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced && InFence.IsMatch(lines[i]))
            {
                yield return (i + 1, lines[i].Trim());
            }
            else if (!fenced)
            {
                foreach (Match span in InBackticks.Matches(lines[i]))
                {
                    yield return (i + 1, span.Value);
                }
            }
        }
    }
}
