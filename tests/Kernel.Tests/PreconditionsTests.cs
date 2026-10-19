using System.Collections.Generic;
using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The precondition table of WP 2.1 over generated models and edits whose preconditions are known by construction; the existing data its
/// counts give; the operations of a plan the change does not make; and the key an element had before the change, which both read.
/// </summary>
public sealed class PreconditionsTests
{
    [Fact]
    [Trait("Category", "fast")]
    public void Each_generated_edit_asserts_exactly_the_preconditions_the_precondition_table_names_for_it() =>
        PreconditionProperties.Edits.Sample(e => Ok(Preconditions.Of(e.BeforeElements, e.AfterElements, e.Change, e.Sites)) == e.Expected,
            print: e => e.Edits, iter: 2000);

    /// <summary>A column a change makes computed declares no type, so it is no Fits; its kind, Computed, arrives with WP 2.1's second
    /// slice. A key, a check or a foreign key the after model holds disabled validates nothing, so it is no precondition.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_made_computed_and_a_rule_left_disabled_assert_nothing()
    {
        var before = Model.Of("T.Id int NOT NULL", "T.Twice int NULL").Elements;
        var after = SortedArray.Of(before.Where(e => e.Key != Model.ColumnKey("T.Twice")).Append(New(Model.ColumnKey("T.Twice"),
            [("Expression", new Value.Script("([Id] * 2)")), ("Nullable", Bool(true))],
            [("ExpressionDependencies", [Model.ColumnKey("T.Id")])])));
        var disabled = Model.Of("T.Id int NOT NULL", "T.PId int NULL", "P.Id int NOT NULL")
            .ForeignKey("FK_T_P", "T", ["PId"], "P", ["Id"], disabled: true).Check("CK_T", "T", "[PId] > 0", ["PId"], disabled: true);

        Assert.Empty(Ok(Preconditions.Of(before, after, Ok(Change.Between(before, after, [])), [])));
        Assert.Empty(Ok(Preconditions.Of([], disabled.Elements, Ok(Change.Between([], disabled.Elements, [])), [])));
    }

    /// <summary>A model that lacks a relationship the precondition table reads, a foreign key's parent here, is model.unresolved.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_foreign_key_without_its_parent_is_model_unresolved()
    {
        var after = SortedArray.Of(Model.Of("T.Id int NOT NULL").Elements.Append(New(Key("ForeignKeyConstraint", "dbo", "FK_T_P"), [],
            [("Columns", [Model.ColumnKey("T.Id")]), ("Host", [Model.TableKey("T")])])));

        Expect.Failed(Preconditions.Of([], after, Ok(Change.Between([], after, [])), []), "model.unresolved");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_key_an_element_had_before_a_change_is_the_one_its_own_or_its_table_s_rename_moved_it_from()
    {
        Renamings.Sample(r => Ok(Change.Between(r.Before, r.After, r.Renames)) is var change
            && r.Kept.All(k => change.KeyBefore(Final(k, r.Entries)) == k), print: r => string.Join(", ", r.Entries), iter: 1000);
        Flips.Sample(f => Ok(Change.Between(f.Before, f.After, [], CaseInsensitive)) is var change
            && f.Before.Where(e => e.Key == f.Flip.Before || e.Key.Parent == f.Flip.Before)
                .All(e => change.KeyBefore(Rebase(e.Key, f.Flip.Before, f.Flip.After)) == e.Key));
    }

    /// <summary>
    /// The existing data the counts give: one state for each precondition counted, populated when a count finds a row and violating when
    /// one finds a row that breaks the rule, a TableEmpty violating exactly when populated; the same whatever order the counts come in.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_existing_data_of_counts_holds_one_state_per_precondition_whatever_their_order()
    {
        Precondition[] pool =
        [
            QuestionCases.TableEmpty("Customer"), QuestionCases.NotNull("Customer.Email"), QuestionCases.Unique("Product", "Code"),
            QuestionCases.Fits("Order.Total", "decimal(18,2)", "int"), QuestionCases.Check("Order", "[Total]>(0)", "Total"),
        ];
        var counts = Gen.Select(Gen.Int[0, pool.Length - 1], Gen.Long[0, 3], Gen.Long[0, 3])
            .Select((p, rows, violating) => new PreconditionCount(pool[p], rows, violating)).Array[0, 8];

        counts.SelectMany(c => Gen.Shuffle(c.ToArray()).Select(shuffled => (c, shuffled))).Sample((given, shuffled) =>
        {
            var existing = ExistingData.Of(given);
            var expected = given.GroupBy(c => c.Precondition).Select(g =>
            {
                var populated = g.Any(c => c.Rows > 0 || c.Violating > 0);
                return new PreconditionState(g.Key, populated,
                    g.Key.Kind is PreconditionKind.TableEmpty ? populated : g.Any(c => c.Violating > 0));
            });

            Assert.Equal(SortedArray.Of(expected), existing.States);
            Assert.Equal(existing, ExistingData.Of(shuffled));
            Assert.All(pool, p => Assert.Equal(existing.States.FirstOrDefault(s => s.Precondition == p), existing.State(p)));
        });
    }

    /// <summary>
    /// Baseline drift is the plan's operations on objects the change neither creates, drops, renames nor alters, nor holds one it does:
    /// over generated edits, a plan altering each table the change touches and creating, dropping and renaming what it does, with
    /// operations on tables no model holds beside them, reports those alone, and no Refresh, UnbindSchemabinding or
    /// RebindSchemabinding; under a case-insensitive collation a table the change alters, named in other letter case, is the change's.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Baseline_drift_is_the_operations_of_the_plan_on_objects_the_change_does_not_touch() =>
        Gen.Select(PreconditionProperties.Edits, Gen.Int[0, 3], Gen.Int[0, 2]).Sample((e, strangers, consequences) =>
        {
            var change = e.Change;
            static ElementKey Top(ElementKey key) => key.Parent is { } parent ? Top(parent) : key;
            var made = change.Altered.Select(a => new PlanOperation(new PlanOperationKind.Alter(), Top(a.Key), []))
                .Concat(change.Created.Select(c => new PlanOperation(new PlanOperationKind.Create(), c.Key, [])))
                .Concat(change.Dropped.Select(d => new PlanOperation(new PlanOperationKind.Drop(), d.Key, [])))
                .Concat(change.Renamed.Select(r => new PlanOperation(new PlanOperationKind.Rename(), r.After, [])));
            var drift = Enumerable.Range(0, strangers).Select(i => new PlanOperation(
                PlanOperationKind.Of(i % 2 == 0 ? "Alter" : "AddSystemVersioning"), Key("Table", "drift", "S" + i), [])).ToList();
            var refreshed = Enumerable.Range(0, consequences).Select(i =>
                new PlanOperation(new PlanOperationKind.Refresh(), Key("View", "drift", "V" + i), []));
            var shouted = change.Altered.Select(a => Top(a.Key)).Where(k => k.Type == "Table").Take(1)
                .Select(k => new PlanOperation(new PlanOperationKind.Alter(), Key("Table", k.Name.Schema!.ToUpperInvariant(),
                    k.Name.Base.ToUpperInvariant()), [])).ToList();
            var plan = new DeployReport(SortedArray.Of(made.Concat(drift).Concat(refreshed).Concat(shouted)), []);

            Assert.Equal(SortedArray.Of(drift), Preconditions.BaselineDrift(plan, change, CaseInsensitive));
            Assert.Equal(SortedArray.Of(drift.Concat(shouted.Where(s => !made.Any(m => m.Key == s.Key)))),
                Preconditions.BaselineDrift(plan, change, Collation.CaseSensitive));
        }, print: x => x.Item1.Edits);
}
