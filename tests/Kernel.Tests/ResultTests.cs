using System.Globalization;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>A result carries a value or the error in its place, and an error passes through Map and Bind.</summary>
public sealed class ResultTests
{
    private static readonly Error Why = new("test.failed", "Failed for the test.", "Nothing to do.");

    [Fact]
    [Trait("Category", "fast")]
    public void A_result_carries_a_value_or_the_error_in_its_place()
    {
        Result<int> ok = 2, failed = Why;
        Assert.Equal(Result.Ok(5), ok.Map(x => x + 3));
        Assert.Equal(Result.Fail<int>(Why), failed.Map(x => x + 3));
        Assert.Equal(Result.Fail<int>(Why), ok.Bind(_ => Result.Fail<int>(Why)));
        Assert.Equal(Result.Ok("2"), ok.Bind(x => Result.Ok(x.ToString(CultureInfo.InvariantCulture))));
        Assert.Equal("test.failed", failed.Match(_ => "", e => e.Code));
    }
}
