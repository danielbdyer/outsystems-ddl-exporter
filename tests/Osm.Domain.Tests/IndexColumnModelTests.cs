using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class IndexColumnModelTests
{
    [Fact]
    public void Create_ShouldFail_WhenOrdinalNotPositive()
    {
        var attribute = AttributeName.Create("Name").Value;
        var column = ColumnName.Create("NAME").Value;

        var result = IndexColumnModel.Create(attribute, column, 0, isIncluded: false, IndexColumnDirection.Ascending);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "index.column.ordinal.invalid");
    }

    [Fact]
    public void Create_ShouldSucceed_WhenOrdinalPositive()
    {
        var attribute = AttributeName.Create("Name").Value;
        var column = ColumnName.Create("NAME").Value;

        var result = IndexColumnModel.Create(attribute, column, 2, isIncluded: true, IndexColumnDirection.Descending);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Ordinal);
    }
}
