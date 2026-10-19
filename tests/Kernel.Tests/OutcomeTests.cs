using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.QuestionCases;

namespace DbChange.Kernel.Tests;

/// <summary>
/// What a prediction says of an environment, from its existing data: blocks when a table a BlockOnPossibleDataLoss site checks holds a row,
/// whatever its rows hold (law 9's kernel form; its publish form, a populated table blocked before and after every NULL is backfilled, is
/// WP 4.5's), or when a row breaks a rule; else applies. A change that blocks ships in two releases when the site stops a tightening of a
/// populated table, else as the operation's recognize: block names it, else it is to be proved; and the operations of the plan the change
/// does not make ride with the outcome as baseline drift.
/// </summary>
public sealed class OutcomeTests
{
    private static readonly string[] Tables = ["T0", "T1", "T2"];

    /// <summary>Preconditions over three tables: a tightening (NotNull or Fits) on T0 and T2, the other kinds on all three.</summary>
    private static readonly Precondition[] Pool =
    [
        NotNull("T0.c"), Fits("T0.d", "int", "bigint"), Fits("T2.d", "nvarchar(50)", "nvarchar(10)"), Unique("T1", "c"),
        ForeignKey("T2", "T0", ("e", "Id")), Check("T0", "[c] > 0", "c"), Check("T1", "[c] > 0", "c"),
    ];

    private static readonly SortedArray<PlanOperation> Drift =
        [new PlanOperation(new PlanOperationKind.Alter(), Key("Table", "dbo", "Elsewhere"), [])];

    /// <summary>
    /// A plan's sites, by table; the change's preconditions, each site's TableEmpty among them or not; and the existing data: whether each
    /// table holds rows, for each precondition on a populated table whether a row breaks it, and whether a TableEmpty state records its
    /// rows as violating, as ExistingData.Of does, or records them clean.
    /// </summary>
    private sealed record Scenario(bool[] Sited, bool[] Asserted, bool[] Populated, bool[] Violating, bool ListsSites, bool CleanSites)
    {
        public SortedArray<BlockOnPossibleDataLossSite> Sites => SortedArray.Of(Tables.Where((_, i) => Sited[i])
            .Select(t => new BlockOnPossibleDataLossSite(Model.TableName(t), "the statement the check on " + t + " protects")));

        public SortedArray<Precondition> Preconditions => SortedArray.Of(Pool.Where((_, i) => Asserted[i])
            .Concat(ListsSites ? Sites.Select(s => TableEmpty(s.Table.Base)) : []));

        public bool Full(Name table) => Populated[System.Array.IndexOf(Tables, table.Base)];

        public ExistingData Existing => new(SortedArray.Of(Tables.Select(t => new PreconditionState(TableEmpty(t), Full(Model.TableName(t)),
                Full(Model.TableName(t)) && !CleanSites))
            .Concat(Pool.Select((p, i) => new PreconditionState(p, Full(p.Table), Full(p.Table) && Violating[i])))));
    }

    private static readonly Gen<Scenario> Scenarios = Gen.Select(Gen.Bool.Array[Tables.Length], Gen.Bool.Array[Pool.Length],
        Gen.Bool.Array[Tables.Length], Gen.Bool.Array[Pool.Length], Gen.Bool, Gen.Bool)
        .Select((s, a, p, v, l, c) => new Scenario(s, a, p, v, l, c));

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "9 BlockOnPossibleDataLoss is data-blind")]
    public void A_populated_table_under_BlockOnPossibleDataLoss_predicts_blocks_however_clean_its_rows() =>
        Scenarios.Select(s => s with { Violating = new bool[Pool.Length], CleanSites = true })
            .Where(s => s.Sites.Any(site => s.Full(site.Table)))
            .Sample(s =>
            {
                var blocks = Assert.IsType<Outcome.Blocks>(Ok(Outcome.Predict(s.Existing, s.Sites, s.Preconditions, Drift)));

                Assert.True(blocks.ByBlockOnPossibleDataLoss);
                Assert.Equal(SortedArray.Of(s.Sites.Where(site => s.Full(site.Table))
                    .Select(site => s.Existing.State(TableEmpty(site.Table.Base))!)), blocks.Because);
            });

    /// <summary>
    /// The outcome over every combination of sites, preconditions and states: blocks exactly when a site's table holds a row or a row
    /// breaks a precondition, because of exactly those states; two releases exactly when a populated table's site stops a tightening the
    /// change makes on it, else the shape the recognize: block names, else prove it; and the plan's baseline drift either way.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_change_blocks_exactly_when_a_checked_table_holds_rows_or_a_row_breaks_a_rule_and_ships_as_its_tightening_allows() =>
        Gen.Select(Scenarios, Gen.OneOfConst<string?>(null, "drop column")).Sample((s, recognized) =>
        {
            var fired = s.Sites.Where(site => s.Full(site.Table)).Select(site => s.Existing.State(TableEmpty(site.Table.Base))!);
            var broken = s.Preconditions.Select(p => s.Existing.State(p)!)
                .Where(state => state.Violating && state.Precondition.Kind is not PreconditionKind.TableEmpty);
            var because = SortedArray.Of(fired.Concat(broken).Distinct());
            var tightened = s.Sites.Any(site => s.Full(site.Table)
                && s.Preconditions.Any(p => p.Table == site.Table && p.Kind is PreconditionKind.NotNull or PreconditionKind.Fits));

            var outcome = Ok(Outcome.Predict(s.Existing, s.Sites, s.Preconditions, Drift, recognized));

            Assert.Equal(Drift, outcome.BaselineDrift);
            Assert.Equal(because.Count == 0
                ? new Outcome.Applies(Drift)
                : new Outcome.Blocks(because, tightened ? new Shipping.TwoReleases()
                    : recognized is { } name ? new Shipping.Recognized(name) : new Shipping.ProveIt(), Drift), outcome);
        });

    /// <summary>A precondition, or a site's table, the existing data holds no state for is refused: a precondition never counted is never
    /// taken as clean.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_precondition_or_a_site_the_existing_data_does_not_hold_is_plan_uncounted() =>
        Gen.Select(Scenarios.Where(s => s.Sites.Count + s.Preconditions.Count > 0), Gen.Int[0, 100]).Sample((s, pick) =>
        {
            var wanted = s.Preconditions.Concat(s.Sites.Select(site => TableEmpty(site.Table.Base))).Distinct().ToList();
            var missing = wanted[pick % wanted.Count];
            var existing = new ExistingData(SortedArray.Of(s.Existing.States.Where(state => state.Precondition != missing)));

            var error = Expect.Failed(Outcome.Predict(existing, s.Sites, s.Preconditions, Drift), "plan.uncounted");

            Assert.Contains(missing.Table.ToString(), error.Message, System.StringComparison.Ordinal);
        });

    /// <summary>
    /// M2's exit 1 in the kernel: a branch that makes Customer.Email NOT NULL blocks on a Customer that has rows, BlockOnPossibleDataLoss
    /// firing on dbo.Customer, and ships in two releases; on an empty Customer it applies; and the plan's operations the branch does not
    /// make are its baseline drift.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void A_branch_making_a_populated_column_NOT_NULL_blocks_in_two_releases_and_applies_on_an_empty_table(bool rows, bool nulls)
    {
        var c = Cases["QST-16"];
        var preconditions = Ok(Preconditions.Of(c.Before, c.After, c.Change, c.Checked));
        var existing = ExistingData.Of(preconditions.Select(p => new PreconditionCount(p, rows ? 6 : 0,
            nulls && p.Kind is PreconditionKind.NotNull ? 2 : 0)));

        var outcome = Ok(Outcome.Predict(existing, c.Checked, preconditions, Drift));

        Assert.Equal(Drift, outcome.BaselineDrift);
        if (!rows)
        {
            Assert.Equal(new Outcome.Applies(Drift), outcome);
            return;
        }

        var blocks = Assert.IsType<Outcome.Blocks>(outcome);
        Assert.True(blocks.ByBlockOnPossibleDataLoss);
        Assert.Equal(new Shipping.TwoReleases(), blocks.Shipping);
        Assert.Equal(nulls ? new[] { TableEmpty("Customer"), NotNull("Customer.Email") } : new[] { TableEmpty("Customer") },
            blocks.Because.Select(state => state.Precondition));
    }

    /// <summary>A rule a row breaks blocks with no site firing, SQL Server refusing the change on existing rows, and ships as the
    /// operation's recognize: block names it, or is to be proved.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_foreign_key_over_an_orphan_blocks_by_constraint_violation_and_ships_as_recognized_or_to_be_proved()
    {
        var c = Cases["QST-50"];
        var preconditions = Ok(Preconditions.Of(c.Before, c.After, c.Change, c.Checked));
        var existing = ExistingData.Of(preconditions.Select(p => new PreconditionCount(p, 4, 1)));

        var recognized = Assert.IsType<Outcome.Blocks>(Ok(Outcome.Predict(existing, c.Checked, preconditions, [], "add foreign key")));
        var unrecognized = Assert.IsType<Outcome.Blocks>(Ok(Outcome.Predict(existing, c.Checked, preconditions, [])));

        Assert.False(recognized.ByBlockOnPossibleDataLoss);
        Assert.Equal(new Shipping.Recognized("add foreign key"), recognized.Shipping);
        Assert.Equal(new Shipping.ProveIt(), unrecognized.Shipping);
        Assert.Equal([ForeignKey("Order", "Customer", ("CustomerId", "Id"))], recognized.Because.Select(state => state.Precondition));
    }
}
