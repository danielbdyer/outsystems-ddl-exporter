using System;
using System.Text;
using System.Text.Json;
using Osm.Json.Deserialization;
using Xunit;

namespace Osm.Json.Tests.Deserialization;

public class BooleanAsZeroOneConverterTests
{
    private static int ReadValue(string json)
    {
        var converter = new BooleanAsZeroOneConverter();
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        Assert.True(reader.Read());
        return converter.Read(ref reader, typeof(int), new JsonSerializerOptions());
    }

    [Theory]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    [InlineData("1", 1)]
    [InlineData("0", 0)]
    [InlineData("\"1\"", 1)]
    [InlineData("\"true\"", 1)]
    [InlineData("\"False\"", 0)]
    public void Read_ShouldNormalizeSupportedValues(string json, int expected)
    {
        var actual = ReadValue(json);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Read_ShouldThrow_WhenValueIsUnsupported()
    {
        var converter = new BooleanAsZeroOneConverter();
        Assert.Throws<JsonException>(() =>
        {
            var bytes = Encoding.UTF8.GetBytes("\"maybe\"");
            var reader = new Utf8JsonReader(bytes);
            Assert.True(reader.Read());
            converter.Read(ref reader, typeof(int), new JsonSerializerOptions());
        });
    }
}
