using System;
using System.Collections.Generic;
using System.Linq;
using DbChangeLite;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class OpenItemTests
{
    private static readonly NamedEnvironment Dev = new("dev", "dev.invalid", "env:DBCHANGE_LITE_TEST_DEV", "profiles/pipeline.publish.xml", new Dictionary<string, string>(), false, 1);

    private static readonly Precondition Empty = Precondition.Of("TableEmpty", "dbo", "Customer", [], null, false, 1);

    private static readonly Precondition Mandatory = Precondition.Of("NotNull", "dbo", "Customer", ["Email"], "SELECT 1 AS present FROM [dbo].[Customer] WHERE [Email] IS NULL", false, 2);

    private static readonly Precondition Key = Precondition.Of("Unique", "dbo", "Customer", ["Email"], "SELECT 1 AS present FROM [dbo].[Customer] GROUP BY [Email] HAVING COUNT(*) > 1", false, 3);

    private static readonly Precondition Reference = Precondition.Of("ForeignKey", "dbo", "Customer", ["AccountId"],
        "SELECT 1 AS present FROM [dbo].[Customer] AS child WHERE child.[AccountId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[Account] AS parent WHERE parent.[Id] = child.[AccountId])", false, 4);

    private static Reading Reading(IReadOnlyList<Precondition> preconditions, IReadOnlyList<PlanOperationAnswer>? unexplained = null, string? missing = null) =>
        new(new PublishProfile("profiles/pipeline.publish.xml", "sha256:0", new DacDeployOptions(), new Dictionary<string, string>()), new Plan([], "", ""), Scripts.Read(""), preconditions,
            new PlanShape(new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>()), [], [], unexplained ?? [], [], missing);

    private static List<OpenItem> Open(Reading reading, Measurement measured, string? intent = null) =>
        Predict.Open(Dev, reading, measured, name => "dbchange-lite predict --env " + name + " --to project:p --onto none", new Source.Project("p"), null, intent, Repo.Src).ToList();

    [Fact]
    [Trait("Category", "fast")]
    public void A_check_whose_select_is_refused_is_read_from_sys_partitions_and_says_so()
    {
        var asked = new List<string>();
        var measured = Predict.Measure(query =>
        {
            asked.Add(query);
            return query == Empty.Query ? new Measured.Refused(229, "The SELECT permission was denied on the object 'Customer'.") : new Measured.Answered(12);
        }, [Empty], false);
        Assert.Equal((true, "sys.partitions", 3), (measured.Checks[0].Fires, measured.Checks[0].ReadBy, measured.Level));
        Assert.Equal([Empty.Query, RowPresence.PartitionsQuery("dbo", "Customer")], asked);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Under_counts_a_check_carries_the_count_it_read_and_without_it_none()
    {
        Assert.Equal(42, Predict.Measure(_ => new Measured.Answered(42), [Empty], true).Checks[0].Count);
        Assert.Null(Predict.Measure(_ => new Measured.Answered(1), [Empty], false).Checks[0].Count);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_lock_wait_a_slow_query_and_a_refusal_each_name_their_cause_and_the_next_step()
    {
        var answers = new Dictionary<string, Measured>
        {
            [Mandatory.Query] = new Measured.TimedOut(5, true),
            [Key.Query] = new Measured.TimedOut(30, false),
            [Reference.Query] = new Measured.Refused(229, "The SELECT permission was denied on the object 'Account', database 'Estate', schema 'dbo'."),
        };
        var measured = Predict.Measure(query => answers[query], [Mandatory, Key, Reference], false);
        Assert.Equal(("unknown", 2), (measured.Outcome, measured.Level));
        var items = Open(Reading([Mandatory, Key, Reference]), measured);
        Assert.Contains("waited 5 seconds for a lock another session holds (Msg 1222)", items[0].Why, StringComparison.Ordinal);
        Assert.Equal("the developer's agent, when no session holds a lock on [dbo].[Customer]", items[0].Next.By);
        Assert.Equal("a DBA", items[1].Next.Person);
        Assert.Contains(Key.Query, items[1].Next.Question, StringComparison.Ordinal);
        Assert.Contains("object 'Account'", items[2].Next.Question, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_known_cause_asks_its_action_and_an_operation_outside_the_intent_or_an_absent_intent_goes_to_the_developer()
    {
        var items = Open(Reading([], [new("Create", "SqlCheckConstraint", "[dbo].[CK_Order_StatusText]", "check-written-with-in"), new("Create", "SqlIndex", "[dbo].[Customer].[IX_Region]", "outside-intent")],
            "delete-entity"), new Measurement([], [], []), "make-mandatory");
        Assert.Equal("Write the check in the OR form SQL Server stores; the difference stands until then.", items[0].Next.Question);
        Assert.Equal(("the developer", "the plan creates SqlIndex [dbo].[Customer].[IX_Region], which make-mandatory does not produce"), (items[1].Next.Person, items[1].Why));
        Assert.Equal("the plan for dev makes none of the operations delete-entity produces", items[2].Why);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_exit_puts_a_block_first_then_no_environment_answering_then_open_items_and_the_message_names_what_blocks()
    {
        var blocks = new EnvironmentAnswer("dev", 3, null, "blocks", "block-on-possible-data-loss", null, null, null, [new CheckAnswer("[dbo].[Customer]", true, "exists", Empty.Query)], [], null, null, null);
        var violates = new EnvironmentAnswer("dev", 3, null, "blocks", "constraint-violation", null, null, null, [], [new PreconditionAnswer("NotNull", "[dbo].[Customer]", ["Email"], true, null, Mandatory.Query)], null, null, null);
        var applies = new EnvironmentAnswer("dev", 3, null, "applies", null, null, null, null, [], [], null, null, null);
        var unread = EnvironmentAnswer.Unread("qa", "SQL Server did not answer for qa.");
        var worst = EnvironmentAnswer.Worst("prod", "blocks if [dbo].[Customer] has rows", "dacpac:release.dacpac", []);
        Assert.Equal(("blocked", "dev blocks: BlockOnPossibleDataLoss stops the publish because [dbo].[Customer] has rows."), Two(Predict.Decide([blocks, unread], 1)));
        Assert.Equal("dev blocks: SQL Server refuses the change because existing rows violate NotNull on [dbo].[Customer] ([Email]).", Predict.Decide([violates], 0).Message);
        Assert.Equal("needs-input", Predict.Decide([applies, unread], 1).Outcome);
        Assert.Equal("unreachable", Predict.Decide([unread, EnvironmentAnswer.Unread("uat", "SQL Server did not answer for uat.")], 2).Outcome);
        Assert.Equal("needs-input", Predict.Decide([unread, worst], 1).Outcome);
        Assert.Equal(("done", "No environment was read; prod blocks if [dbo].[Customer] has rows."), Two(Predict.Decide([worst], 0)));
        Assert.Equal(0, unread.Level);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_confidence_sentence_names_each_environment_asked_for_at_the_level_it_reached()
    {
        var rows = new[]
        {
            new EnvironmentAnswer("dev", 4, null, "applies", null, null, null, null, [], [], null, null, null),
            new EnvironmentAnswer("qa", 3, null, "applies", null, null, null, null, [], [], null, null, null),
            EnvironmentAnswer.Unread("uat", "SQL Server did not answer for uat."),
            EnvironmentAnswer.Worst("prod", "applies", "dacpac:release.dacpac", []),
        };
        Assert.Equal("Proven on dev; Measured on qa; uat not read; prod assumed.", Predict.Confidence(rows));
    }

    private static (string, string) Two((string Outcome, string Message, EnvironmentAnswer? Blocking) decided) => (decided.Outcome, decided.Message);
}
