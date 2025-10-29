using Osm.Emission.Formatting;

namespace Osm.Emission.Tests;

public class SqlLiteralFormatterTests
{
    [Fact]
    public void FormatValue_WithNull_ReturnsNullLiteral()
    {
        var formatter = new SqlLiteralFormatter();

        var result = formatter.FormatValue(null);

        Assert.Equal("NULL", result);
    }

    [Theory]
    [InlineData("O'Brien", "N'O''Brien'")]
    [InlineData("", "N''")]
    public void FormatValue_WithString_EscapesAndPrefixesUnicode(string input, string expected)
    {
        var formatter = new SqlLiteralFormatter();

        var result = formatter.FormatValue(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatValue_WithBinary_ReturnsHexLiteral()
    {
        var formatter = new SqlLiteralFormatter();
        var bytes = new byte[] { 0x0A, 0xBC, 0x00 };

        var result = formatter.FormatValue(bytes);

        Assert.Equal("0x0ABC00", result);
    }

    [Fact]
    public void EscapeString_EscapesSingleQuotes()
    {
        var formatter = new SqlLiteralFormatter();

        var result = formatter.EscapeString("It's fine");

        Assert.Equal("It''s fine", result);
    }
}
