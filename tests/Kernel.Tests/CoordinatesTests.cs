using System.Collections.Generic;
using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.QuestionCases;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The coordinate rules of WP 2.1 (the Facsimile's S3; law 2 of v2's synthetic copy, coordinate totality): a precondition on a table
/// the change creates is answered from the change; a column the change creates holds its default, or NULL, in every existing row; an object
/// the change renames or moves is counted under the key it had; anything else is counted where the environment holds it; and an object the
/// environment does not hold is refused by name, never counted as empty.
/// </summary>
public sealed class CoordinatesTests
{
    /// <summary>
    /// The catalog's cases of the coordinate rules, each precondition located in the environment the case reads (its base model). QST-45
    /// and QST-58: every precondition on a table the change creates is answered. QST-46, QST-62 and QST-100: a column the change creates
    /// is counted nowhere, its existing rows holding its default or NULL. QST-101: the new parent of a foreign key is an object the change
    /// creates, and only the child is counted. QST-102 and QST-103: a renamed column and a moved table are counted under their old keys.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-45", 0)]
    [InlineData("QST-45", 1)]
    [InlineData("QST-46", 0)]
    [InlineData("QST-58", 0)]
    [InlineData("QST-58", 1)]
    [InlineData("QST-62", 0)]
    [InlineData("QST-62", 1)]
    [InlineData("QST-100", 0)]
    [InlineData("QST-101", 0)]
    [InlineData("QST-101", 1)]
    [InlineData("QST-102", 0)]
    [InlineData("QST-102", 1)]
    [InlineData("QST-103", 0)]
    [InlineData("QST-104", 0)]
    public void A_case_of_the_coordinate_rules_is_answered_or_counted_where_the_environment_holds_each_object(string id, int at)
    {
        var c = Cases[id];
        var precondition = SortedArray.Of(c.Expected)[at];

        Assert.Equal(Expected[(id, at)](precondition), Ok(Coordinates.Of(precondition, c.Change, c.Before)));
    }

    /// <summary>QST-104: a precondition on a column the environment lacks, and the change neither creates nor renames, is refused by name;
    /// reading it as a column without NULLs would predict a clean change.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_the_environment_lacks_and_the_change_does_not_create_is_refused_by_name()
    {
        var c = Cases["QST-104"];
        var dev = Pg.Elements;

        var error = Expect.Failed(Coordinates.Of(NotNull("Customer.Nickname"), c.Change, dev), "plan.missing-object");

        Assert.Contains(Model.ColumnKey("Customer.Nickname").ToString(), error.Message, System.StringComparison.Ordinal);
        Assert.IsType<Coordinates.Counted>(Ok(Coordinates.Of(TableEmpty("Customer"), c.Change, dev)));
    }

    /// <summary>
    /// Over generated edits, with the environment at the model before them: a precondition is answered exactly when the change creates its
    /// table; each object a counted one reads is held under the key it had before the change, renamed or moved, or is a column the change
    /// creates with the default the edit gave it; and an environment lacking any held object refuses the precondition, naming it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_precondition_is_answered_by_the_change_counted_where_each_object_was_or_refused_by_name() =>
        PreconditionProperties.Edits.Sample(e =>
        {
            var change = e.Change;
            foreach (var precondition in e.Expected)
            {
                var located = Ok(Coordinates.Of(precondition, change, e.BeforeElements));
                var created = change.Created.Any(x => x.Key == Model.TableKey(precondition.Table.Schema + "." + precondition.Table.Base));
                Assert.Equal(created, located is Coordinates.Answered);
                if (located is not Coordinates.Counted counted)
                {
                    Assert.Equal(new PreconditionCount(precondition, 0, 0), ((Coordinates.Answered)located).Count);
                    continue;
                }

                foreach (var place in counted.Places)
                {
                    Assert.Equal(e.Was.GetValueOrDefault(place.After), place.Environment);
                    Assert.Equal(e.Defaults.GetValueOrDefault(place.After), place.Default);
                    if (place.Environment is { } held)
                    {
                        var lacking = SortedArray.Of(e.BeforeElements.Where(x => x.Key != held));
                        var error = Expect.Failed(Coordinates.Of(precondition, change, lacking), "plan.missing-object");
                        Assert.Contains(held.ToString(), error.Message, System.StringComparison.Ordinal);
                    }
                }
            }
        }, print: e => e.Edits, iter: 500);

    private static readonly Dictionary<(string, int), System.Func<Precondition, Coordinates>> Expected = new()
    {
        [("QST-45", 0)] = Answered,
        [("QST-45", 1)] = Answered,
        [("QST-46", 0)] = p => Counted(p, Held(Model.TableKey("Customer")), Made(Model.ColumnKey("Customer.ExternalRef"))),
        [("QST-58", 0)] = Answered,
        [("QST-58", 1)] = Answered,
        [("QST-62", 0)] = p => Counted(p, Held(Model.TableKey("Product")), Made(Model.ColumnKey("Product.Qty"))),
        [("QST-62", 1)] = p => Counted(p, Held(Model.TableKey("Product")), Made(Model.ColumnKey("Product.Rank"), "((0))")),
        [("QST-100", 0)] = p => Counted(p, Held(Model.TableKey("Product")), Made(Model.ColumnKey("Product.Sku"), "(N'NONE')")),
        [("QST-101", 0)] = p => Counted(p, Held(Model.TableKey("Order")), Held(Model.ColumnKey("Order.OrderTypeId")),
            Made(Model.TableKey("OrderType")), Made(Model.ColumnKey("OrderType.Id"))),
        [("QST-101", 1)] = Answered,
        [("QST-102", 0)] = p => Counted(p, Held(Model.TableKey("Customer"))),
        [("QST-102", 1)] = p => Counted(p, Held(Model.TableKey("Customer")),
            new Place(Model.ColumnKey("Customer.MobileNumber"), Model.ColumnKey("Customer.ContactPhone"), null)),
        [("QST-103", 0)] = p => Counted(p, new Place(Model.TableKey("archive.Category"), Model.TableKey("Category"), null),
            new Place(Model.ColumnKey("archive.Category.Code"), Model.ColumnKey("Category.Code"), null)),
        [("QST-104", 0)] = p => Counted(p, Held(Model.TableKey("Customer"))),
    };

    private static Coordinates Answered(Precondition p) => new Coordinates.Answered(new PreconditionCount(p, 0, 0));

    private static Coordinates Counted(Precondition p, params Place[] places) => new Coordinates.Counted(p, SortedArray.Of(places));

    /// <summary>A table or a column the environment holds under its own key.</summary>
    private static Place Held(ElementKey key) => new(key, key, null);

    /// <summary>A table or a column the change creates; a column with the default its table's existing rows hold, or none for
    /// NULL.</summary>
    private static Place Made(ElementKey key, string? fill = null) => new(key, null, fill);
}
