using System;
using System.Globalization;
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
    public void FormatValue_WithDateOnly_UsesCast()
    {
        var formatter = new SqlLiteralFormatter();
        var value = new DateOnly(2024, 1, 15);

        var result = formatter.FormatValue(value);

        Assert.Equal("CAST('2024-01-15' AS date)", result);
    }

    [Fact]
    public void FormatValue_WithTimeOnly_UsesCast()
    {
        var formatter = new SqlLiteralFormatter();
        var value = TimeOnly.FromTimeSpan(TimeSpan.ParseExact("05:06:07.1234567", "c", CultureInfo.InvariantCulture));

        var result = formatter.FormatValue(value);

        Assert.Equal("CAST('05:06:07.1234567' AS time(7))", result);
    }

    [Fact]
    public void FormatValue_WithDateTime_UsesDatetime2Cast()
    {
        var formatter = new SqlLiteralFormatter();
        var value = new DateTime(2024, 1, 15, 13, 30, 5, 123, DateTimeKind.Utc).AddTicks(4_567);

        var result = formatter.FormatValue(value);

        Assert.Equal("CAST('2024-01-15 13:30:05.1234567' AS datetime2(7))", result);
    }

    [Fact]
    public void FormatValue_WithDateTimeOffset_UsesDatetimeOffsetCast()
    {
        var formatter = new SqlLiteralFormatter();
        var value = new DateTimeOffset(2024, 1, 15, 13, 30, 5, TimeSpan.FromHours(-3)).AddTicks(1_234_567);

        var result = formatter.FormatValue(value);

        Assert.Equal("CAST('2024-01-15 13:30:05.1234567-03:00' AS datetimeoffset(7))", result);
    }

    [Fact]
    public void EscapeString_EscapesSingleQuotes()
    {
        var formatter = new SqlLiteralFormatter();

        var result = formatter.EscapeString("It's fine");

        Assert.Equal("It''s fine", result);
    }
}
