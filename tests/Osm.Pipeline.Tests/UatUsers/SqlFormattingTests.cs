using Osm.Pipeline.UatUsers;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class SqlFormattingTests
{
    [Theory]
    [InlineData("User]Name", "[User]]Name]")]
    [InlineData("EndsWith]", "[EndsWith]]]")]
    public void QuoteIdentifier_DoublesClosingBrackets(string input, string expected)
    {
        var result = SqlFormatting.QuoteIdentifier(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void QuoteIdentifier_FallsBackToEmptyBrackets(string? input)
    {
        var result = SqlFormatting.QuoteIdentifier(input!);
        Assert.Equal("[]", result);
    }

    [Theory]
    [InlineData("O'Brian", "'O''Brian'")]
    [InlineData("Value with 'quote'", "'Value with ''quote'''")]
    public void SqlStringLiteral_DoublesApostrophesAndWraps(string value, string expected)
    {
        var result = SqlFormatting.SqlStringLiteral(value);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SqlStringLiteral_FallsBackToEmptyLiteral(string? value)
    {
        var result = SqlFormatting.SqlStringLiteral(value!);
        Assert.Equal("''", result);
    }
}
