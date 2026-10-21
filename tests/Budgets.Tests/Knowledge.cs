using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// knowledge/, the files a developer's Copilot session reads in the SSDT repository and a reviewer reads beside the pull request
/// (V3_INSTRUCTION_ARCHITECTURE.md §5.2). The knowledge budget of ci/budgets.json counts every hand-written file under knowledge/ and
/// no generated one, under the ceiling WP 7.2's row of V3_MILESTONES.md sets. knowledge/authoring.md runs S0 to S8 in order, each
/// step naming the dbchange verb that does it; its first step splits a compound request into ordered pull requests (R28); and it
/// states the rules a remedy and a deploy script obey. knowledge/README.md says what a session does when dbchange is not on the
/// machine (R27). knowledge/description.md holds the ten sections between its template markers, which WP 5.1's
/// PullRequestDescription and WP 7.3's pull request templates read. No knowledge file writes a check constraint with IN, which SQL
/// Server stores as a chain of ORs, so every plan drops and creates it again (the operator's ruling of 2026-10-19).
/// </summary>
public sealed class Knowledge
{
    private const string Authoring = "knowledge/authoring.md";

    private const string Entry = "knowledge/README.md";

    private const string Description = "knowledge/description.md";

    private static readonly Regex Step = new(@"^## (S\d+) · ", RegexOptions.CultureInvariant);

    private static readonly Regex VerbCited = new(@"`dbchange ([a-z][\w-]*)", RegexOptions.CultureInvariant);

    /// <summary>WP 7.2's done-when in V3_MILESTONES.md: the knowledge budgets hold (≤ 11,000 lines; ≤ 90 per operation).</summary>
    private static readonly Regex PlannedCeiling = new(@"≤ ([\d,]+) lines", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L3")]
    public void The_knowledge_budget_counts_every_hand_written_file_under_knowledge_and_no_generated_one_within_the_plan_s_ceiling()
    {
        var budget = JsonNode.Parse(Repository.Read("ci/budgets.json"))!["budgets"]!["knowledge"];
        Assert.NotNull(budget);
        var counted = Budgets.Counted(budget).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var rows = Documents.Rows.Where(r => r.Path.StartsWith("knowledge/", StringComparison.Ordinal)).ToList();
        var plan = Repository.Lines("V3_MILESTONES.md").Single(l => l.StartsWith("| 7.2 |", StringComparison.Ordinal));
        var planned = int.Parse(PlannedCeiling.Match(plan).Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        Assert.NotEmpty(rows);
        Assert.Empty(rows.Where(r => r.Kind == "hand" && !counted.Contains(r.Path))
            .Select(r => r.Path + " is hand-written and the knowledge budget does not count it"));
        Assert.Empty(rows.Where(r => r.Kind == "generated" && counted.Contains(r.Path))
            .Select(r => r.Path + " is generated and the knowledge budget counts it"));
        Assert.True((int)budget["ceiling"]! <= planned, "ci/budgets.json budgets.knowledge.ceiling exceeds the "
            + planned.ToString(CultureInfo.InvariantCulture) + " lines WP 7.2's row of V3_MILESTONES.md sets");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_authoring_path_runs_S0_to_S8_in_order_and_each_step_names_the_verb_that_does_it()
    {
        var steps = Steps();

        Assert.Equal(Enumerable.Range(0, 9).Select(n => "S" + n.ToString(CultureInfo.InvariantCulture)), steps.Select(s => s.Name));
        Assert.Empty(steps.Where(s => !VerbCited.Matches(s.Text).Any(m => DbChange.Cli.Contract.Verbs.Any(v => v.Name == m.Groups[1].Value)))
            .Select(s => Authoring + " " + s.Name + " names no dbchange verb of the verb table"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_first_step_of_the_authoring_path_splits_a_compound_request_into_ordered_pull_requests()
    {
        var first = string.Join(' ', Steps()[0].Text.Split('\n').Skip(1).TakeWhile(l => l.Trim().Length > 0));

        Assert.Matches(@"(?i)\bcompound\b", first);
        Assert.Matches(@"(?i)\bpull requests\b", first);
        Assert.Matches(@"(?i)\border\b", first);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Without_dbchange_on_the_machine_a_session_stops_and_the_gate_proves_the_pull_request_marked_provisional()
    {
        var section = Section(Entry, h => h.Contains("`dbchange` is not on this machine", StringComparison.Ordinal));

        Assert.Matches(@"(?i)\bstop\b", section);
        Assert.Matches(@"(?i)\bprovisional\b", section);
        Assert.Contains("`dbchange gate`", section, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_description_template_holds_the_title_and_the_ten_sections_in_order_between_its_markers()
    {
        var lines = Repository.Lines(Description);
        var begin = Array.IndexOf(lines, "<!-- template:begin -->");
        var end = Array.IndexOf(lines, "<!-- template:end -->");
        Assert.True(begin >= 0 && end > begin, Description + " holds no <!-- template:begin --> line before a <!-- template:end --> line");
        var headings = lines[(begin + 1)..end].Where(l => l.StartsWith('#')).ToList();

        Assert.StartsWith("# <object>: ", headings[0], StringComparison.Ordinal);
        Assert.Equal(
            ["Verdict", "Intent", "What changes", "Before promoting", "The data", "How it ships", "What proving showed",
             "After deploy — check", "How to roll this back", "Not checked / still open"],
            headings.Skip(1).Select(h => h.StartsWith("## ", StringComparison.Ordinal) ? h[3..] : "not a section: " + h));
    }

    /// <summary>
    /// Each rule V3_MILESTONES.md §13 gives the authoring path for a remedy and a deploy script, as the words one paragraph or list item
    /// of knowledge/authoring.md must hold together.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("a remedy tests a predicate and never a literal value", @"(?i)\bpredicate\b", @"(?i)\bliteral value\b", @"(?i)\bremedy\b")]
    [InlineData("a remedy checks its own preconditions and throws before any write", @"(?i)\bpreconditions\b", @"\bTHROW\b",
        @"(?i)\bbefore any write\b")]
    [InlineData("a failed deploy leaves a deploy script's work in place, so the script runs again safely", @"(?i)\bpost-deploy\b",
        @"(?i)\bnot rolled back\b", @"(?i)\brun again\b")]
    [InlineData("a check constraint is written in the OR form SQL Server stores", @"(?i)\bcheck constraint\b", @"`OR`", @"`IN`")]
    public void The_authoring_path_states_each_rule_a_remedy_and_a_deploy_script_obey(string rule, string first, string second,
        string third) =>
        Assert.True(Documents.Blocks(Authoring).Any(b => new[] { first, second, third }.All(p => Regex.IsMatch(b.Text, p))),
            Authoring + " states nowhere, in one paragraph, that " + rule);

    public static TheoryData<string> KnowledgeDocuments =>
        new(Documents.Tool.Where(d => d.StartsWith("knowledge/", StringComparison.Ordinal)));

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(KnowledgeDocuments))]
    public void No_knowledge_document_writes_a_check_constraint_with_IN(string document) =>
        Assert.Empty(ChecksWithIn(Repository.Read(document)).Select(c => document + ": '" + c
            + "' writes a check with IN, which every plan drops and creates again; write the OR form SQL Server stores"));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("`CONSTRAINT CK_Order_Status CHECK ([Status] IN ('A', 'B'))`", true)]
    [InlineData("```sql\nALTER TABLE dbo.[Order] ADD CONSTRAINT CK_Order_Status\n    CHECK ([Status] NOT IN ('X'));\n```", true)]
    [InlineData("`CHECK ([Status] = 'A' OR [Status] = 'B')`", false)]
    [InlineData("a check constraint written with `IN` is stored as `OR`s", false)]
    [InlineData("```sql\nALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer;\nSELECT 1 WHERE 1 IN (1);\n```", false)]
    public void A_check_written_with_IN_is_found_in_code_and_never_in_prose(string text, bool found) =>
        Assert.Equal(found, ChecksWithIn(text).Any());

    private static readonly Regex Code = new(@"```[^\n]*\n(.*?)```|`([^`\n]+)`", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex CheckWithIn = new(@"\bCHECK\s*\([^;]*?\bIN\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Each check constraint written with IN in a fenced block or a backticked span of a document.</summary>
    private static IEnumerable<string> ChecksWithIn(string text) => Code.Matches(text)
        .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
        .SelectMany(code => CheckWithIn.Matches(code).Select(c => c.Value.ReplaceLineEndings(" ")));

    /// <summary>The steps of knowledge/authoring.md, each with its name and the text from its heading to the next heading.</summary>
    private static IReadOnlyList<(string Name, string Text)> Steps()
    {
        var lines = Repository.Lines(Authoring);
        return lines.Select((l, i) => (Match: Step.Match(l), Index: i)).Where(x => x.Match.Success)
            .Select(x => (x.Match.Groups[1].Value, string.Join('\n', lines.Skip(x.Index).TakeWhile((l, i) => i == 0 || !l.StartsWith("## ",
                StringComparison.Ordinal)))))
            .ToList();
    }

    /// <summary>The text of the section of a document whose heading the predicate picks out, to the next heading of its level.</summary>
    private static string Section(string document, Func<string, bool> heading)
    {
        var lines = Repository.Lines(document);
        var start = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal) && heading(l));
        Assert.True(start >= 0, document + " holds no section with the heading the test reads");
        return string.Join('\n', lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)));
    }
}
