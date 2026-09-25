using System;
using System.Collections.Generic;
using System.Linq;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// What an aggregate query measured is a value (finding ARCH-16): two measurements of equal rows are equal whatever order SQL Server
/// returned the rows in, as M3's evidence and M4's comparison of a prediction with a proof need; and each case is read through Match.
/// </summary>
public sealed class MeasurementTests
{
    [Fact]
    [Trait("Category", "fast")]
    public void Two_measurements_of_equal_rows_are_equal_and_of_different_rows_are_not()
    {
        var first = new SqlServer.Measurement.Answered("dbo.Customer.Email Fits", SortedArray.Of(SqlServer.Row.Of(3, null), SqlServer.Row.Of(1, 2)));
        var again = new SqlServer.Measurement.Answered("dbo.Customer.Email Fits", SortedArray.Of((IEnumerable<SqlServer.Row>)[SqlServer.Row.Of(1, 2), SqlServer.Row.Of(3, null)]));
        var other = new SqlServer.Measurement.Answered("dbo.Customer.Email Fits", SortedArray.Of(SqlServer.Row.Of(1, 2), SqlServer.Row.Of(3, 0)));

        Assert.Equal(first, again);
        Assert.Equal(first.GetHashCode(), again.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.NotEqual(SqlServer.Row.Of(1, 2), SqlServer.Row.Of(1, 2, null));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_measurement_is_read_through_Match_in_each_of_its_cases()
    {
        SqlServer.Measurement[] measured =
        [
            new SqlServer.Measurement.Answered("rows", SortedArray.Of(SqlServer.Row.Of(7))),
            new SqlServer.Measurement.Failed("conversion", 245, null),
        ];

        Assert.Equal(["rows answered 1 row", "conversion failed with Msg 245"], measured.Select(m => m.Match(
            answered => answered.Site + " answered " + answered.Rows.Count + " row", failed => failed.Site + " failed with Msg " + failed.Number)));
    }
}
