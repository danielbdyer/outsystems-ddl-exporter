using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// The milestones' exits (DECISIONS.md, 2026-09-25): a test that runs one declares [Trait("Exit", "M&lt;n&gt;.&lt;k&gt;")], and each such trait
/// names an exit V3_MILESTONES.md lists; a milestone is complete when each of its exits has a test or a NEXT.md line naming the
/// command a person runs, and what a milestone's completion admits (ValuesResolve, Citations) follows from that alone.
/// </summary>
public sealed class Milestones
{
    [Fact]
    [Trait("Category", "fast")]
    public void Every_Exit_trait_names_an_exit_of_the_plan()
    {
        var exits = Documents.EveryExit.ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(exits);
        Assert.Empty(TestTraits.All.SelectMany(t => t.Values("Exit").Where(e => !exits.Contains(e)).Select(e => t.FullName + " declares Exit " + e + ", which V3_MILESTONES.md does not list")));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_exit_reader_takes_the_numbered_list_after_Exit_and_reads_a_paragraph_exit_as_exit_1()
    {
        string[] plan =
        [
            "## 6. M0 — Foundation (day 1)", "", "**Exit.** M0 closes once the operator installs the hooks. What shows each exit: 1, CI; 2, the counts.",
            "1. The build is clean.", "2. The archive builds.", "",
            "## 7. M1 — Read", "", "**Exit.**", "1. One.", "   continued on a second line.", "2. Two.", "3. Three.", "",
            "## 13. M7 — Agent instructions", "", "**Exit.** The package check is clean.", "",
            "## 14. M8 — One tool", "", "**M8.** Whether the cutover tools are ported is decided.", "",
        ];

        var exits = Documents.ReadExits(plan);

        Assert.Equal([0, 1, 7], exits.Keys.Order());
        Assert.Equal([1, 2], exits[0]);
        Assert.Equal([1, 2, 3], exits[1]);
        Assert.Equal([1], exits[7]);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("M1.1;M1.2", "", true)]
    [InlineData("M1.1", "", false)]
    [InlineData("M1.1", "- Run the laptop check against Dev (M1 exit 2, spikes S1 and S3) with `estate check drift --target env:dev --at v1`.", true)]
    [InlineData("M1.1", "- Run the laptop check against Dev (M1 exit 2, spikes S1 and S3) with the tool folder built at `e0a4db94`.", false)]
    [InlineData("M1.1", "- Run `estate check drift --target env:dev --at v1` for M2 exit 2.", false)]
    public void A_milestone_is_complete_when_each_exit_has_a_test_or_a_NEXT_md_line_naming_it_beside_the_estate_command_a_person_runs(string declared, string next, bool complete)
    {
        var exits = new Dictionary<int, IReadOnlyList<int>> { [1] = [1, 2] };

        Assert.Equal(complete, Documents.Complete(1, exits, declared.Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal), [next]));
        Assert.False(Documents.Complete(8, exits, new HashSet<string>(StringComparer.Ordinal) { "M8.1" }, [next]));
    }
}
