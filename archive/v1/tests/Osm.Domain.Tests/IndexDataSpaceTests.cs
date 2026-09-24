using Osm.Domain.Model;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class IndexDataSpaceTests
{
    [Fact]
    public void Create_ShouldTrimValues()
    {
        var result = IndexDataSpace.Create("  PRIMARY  ", "  ROWS_FILEGROUP  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("PRIMARY", result.Value.Name);
        Assert.Equal("ROWS_FILEGROUP", result.Value.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldFail_WhenNameMissing(string? name)
    {
        var result = IndexDataSpace.Create(name, "ROWS");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "index.dataSpace.name.invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldFail_WhenTypeMissing(string? type)
    {
        var result = IndexDataSpace.Create("PRIMARY", type);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "index.dataSpace.type.invalid");
    }
}
