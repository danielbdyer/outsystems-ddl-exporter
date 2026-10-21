using System;
using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.QuestionCases;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The outcome over the kinds only a plan's statements show (WP 2.1's second slice): a Scan and a Rebuild read every row and fail on none,
/// so no count of theirs blocks; a KeyLength, a Temporal and a SeedDrift block when a counted row breaks them; and a Computed or an
/// Authored is declared by a person, never counted, so the prediction refuses one with no declared state (plan.undeclared) and reads a
/// declared one as it reads a counted one.
/// </summary>
public sealed class OutcomeKindsTests
{
    private static readonly Precondition Scanned = new(Model.TableName("Order"), [], new PreconditionKind.Scan());

    private static readonly Precondition Rebuilt = new(Model.TableName("Category"), Names(["Id"]), new PreconditionKind.Rebuild());

    private static readonly Precondition LongKey = new(Model.TableName("N"), Names(["Big"]),
        new PreconditionKind.KeyLength(Ok(Name.Of("IX_N_Big")), false));

    private static readonly Precondition Period = new(Model.TableName("Booking"), Names(["ValidFrom", "ValidTo"]),
        new PreconditionKind.Temporal(Ok(Name.Of("ValidFrom")), Ok(Name.Of("ValidTo")), null));

    private static readonly Precondition Seed = new(Model.TableName("Status"), Names(["Id"]),
        new PreconditionKind.SeedDrift(Names(["Code"]), ScriptPart.PostDeploy, 1));

    private static readonly Precondition RefNum = new(Model.TableName("T"), Names(["RefNum"]),
        new PreconditionKind.Computed("(CONVERT([int],[Ref]))"));

    private static readonly Precondition Backfill = new(Model.TableName("Customer"), Names(["Email"]),
        new PreconditionKind.Authored(ScriptPart.PreDeploy, 4));

    [Fact]
    [Trait("Category", "fast")]
    public void A_Scan_or_a_Rebuild_applies_whatever_its_count_of_rows_and_of_rows_that_break_it() =>
        Gen.Select(Gen.Long[0, 1000], Gen.Long[0, 1000], Gen.Bool).Sample((rows, violating, rebuild) =>
        {
            var precondition = rebuild ? Rebuilt : Scanned;
            var existing = ExistingData.Of([new PreconditionCount(precondition, rows, violating)]);

            Assert.Equal((rows > 0 || violating > 0, false), (existing.State(precondition)!.Populated, existing.State(precondition)!.Violating));
            Assert.Equal(new Outcome.Applies([]), Ok(Outcome.Predict(existing, [], [precondition], [])));
        });

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("KeyLength")]
    [InlineData("Temporal")]
    [InlineData("SeedDrift")]
    public void A_KeyLength_a_Temporal_or_a_SeedDrift_blocks_exactly_when_a_counted_row_breaks_it(string kind)
    {
        var precondition = kind switch { "KeyLength" => LongKey, "Temporal" => Period, _ => Seed };
        var (clean, broken) = (ExistingData.Of([new PreconditionCount(precondition, 5, 0)]),
            ExistingData.Of([new PreconditionCount(precondition, 5, 1)]));

        Assert.Equal(new Outcome.Applies([]), Ok(Outcome.Predict(clean, [], [precondition], [])));
        Assert.Equal(new Outcome.Blocks([broken.State(precondition)!], new Shipping.ProveIt(), []),
            Ok(Outcome.Predict(broken, [], [precondition], [])));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Computed")]
    [InlineData("Authored")]
    public void A_Computed_or_an_Authored_is_refused_until_a_person_declares_its_state_and_then_reads_as_declared(string kind)
    {
        var precondition = kind == "Computed" ? RefNum : Backfill;
        var (clean, broken) = (new PreconditionState(precondition, true, false), new PreconditionState(precondition, true, true));

        var error = Expect.Failed(Outcome.Predict(ExistingData.Of([]), [], [precondition], []), "plan.undeclared");

        Assert.Contains(precondition.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Equal(new Outcome.Applies([]), Ok(Outcome.Predict(ExistingData.Of([], [clean]), [], [precondition], [])));
        Assert.Equal(new Outcome.Blocks([broken], new Shipping.ProveIt(), []),
            Ok(Outcome.Predict(ExistingData.Of([], [broken]), [], [precondition], [])));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Existing_data_takes_no_count_of_a_declared_kind_and_no_declared_state_of_a_counted_one()
    {
        Assert.Throws<ArgumentException>("counts", () => ExistingData.Of([new PreconditionCount(RefNum, 3, 0)]));
        Assert.Throws<ArgumentException>("declared", () => ExistingData.Of([], [new PreconditionState(LongKey, true, false)]));
    }
}
