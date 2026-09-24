using System.Globalization;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>A result carries a value or the refusal in its place, and a refusal passes through Map and Bind.</summary>
public sealed class ResultTests
{
    private static readonly Refusal Why = new("test.refused", "Refused for the test.", "Nothing to do.");

    [Fact]
    [Trait("Category", "fast")]
    public void A_result_carries_a_value_or_the_refusal_in_its_place()
    {
        Result<int> ok = 2, refused = Why;
        Assert.Equal(Result.Ok(5), ok.Map(x => x + 3));
        Assert.Equal(Result.Refuse<int>(Why), refused.Map(x => x + 3));
        Assert.Equal(Result.Refuse<int>(Why), ok.Bind(_ => Result.Refuse<int>(Why)));
        Assert.Equal(Result.Ok("2"), ok.Bind(x => Result.Ok(x.ToString(CultureInfo.InvariantCulture))));
        Assert.Equal("test.refused", refused.Match(_ => "", r => r.Code));
    }
}
