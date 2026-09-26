using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CsCheck;
using Xunit;

namespace DbChange.Kernel.Tests;

/// <summary>A result carries a value or the error in its place, and an error passes through Map and Bind.</summary>
public sealed class ResultTests
{
    private static readonly Error Why = new("internal.failed", "Failed for the test.", "Nothing to do.");

    private static readonly Error Later = new("internal.later", "Failed after the first error.", "Nothing to do.");

    [Fact]
    [Trait("Category", "fast")]
    public void Map_and_Bind_apply_the_function_to_a_value_and_pass_an_error_through_unchanged()
    {
        Result<int> ok = 2, failed = Why;
        Assert.Equal(Result.Ok(5), ok.Map(x => x + 3));
        Assert.Equal(Result.Fail<int>(Why), failed.Map(x => x + 3));
        Assert.Equal(Result.Fail<int>(Why), ok.Bind(_ => Result.Fail<int>(Why)));
        Assert.Equal(Result.Ok("2"), ok.Bind(x => Result.Ok(x.ToString(CultureInfo.InvariantCulture))));
        Assert.Equal("internal.failed", failed.Match(_ => "", e => e.Code));
    }

    /// <summary>Failed is false with the value for a result that holds one, and true with the error for one that does not.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Failed_gives_a_result_s_value_or_its_error()
    {
        Result<int> ok = 2, failed = Why;

        Assert.False(ok.Failed(out var value, out var none));
        Assert.True(failed.Failed(out _, out var error));
        Assert.Equal((2, null), (value, none));
        Assert.Same(Why, error);
    }

    /// <summary>
    /// Result.All over results built from values and the place of the first error, if any: every value, in the order given, when each
    /// result holds one; else the first error in that order, whatever errors follow it, with no result after it read. io relies on the
    /// last: a SQLCMD reference after one that does not resolve is never read (VALUES.md X2).
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void All_is_every_value_in_order_or_the_first_error_and_reads_no_result_after_it() =>
        Gen.Select(Gen.Int.Array[0, 8], Gen.Int[-1, 8], Gen.Bool.Array[8]).Sample((values, first, laterFails) =>
        {
            var fails = first < values.Length ? first : -1;
            var made = 0;
            IEnumerable<Result<int>> Results()
            {
                for (var i = 0; i < values.Length; i++)
                {
                    made = i + 1;
                    yield return i == fails ? Result.Fail<int>(Why)
                        : fails >= 0 && i > fails && laterFails[i] ? Result.Fail<int>(Later)
                        : Result.Ok(values[i]);
                }
            }

            var all = Result.All(Results());
            return fails < 0
                ? all is Result<IReadOnlyList<int>>.Ok ok && ok.Value.SequenceEqual(values) && made == values.Length
                : all == Result.Fail<IReadOnlyList<int>>(Why) && made == fails + 1;
        });
}
