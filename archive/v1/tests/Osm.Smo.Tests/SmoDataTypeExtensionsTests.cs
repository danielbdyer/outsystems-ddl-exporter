using Microsoft.SqlServer.Management.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public sealed class SmoDataTypeExtensionsTests
{
    [Fact]
    public void GetDeclaredPrecision_ReturnsRequestedPrecisionForDecimal()
    {
        var dataType = DataType.Decimal(37, 8);

        Assert.Equal(37, dataType.GetDeclaredPrecision());
        Assert.Equal(8, dataType.GetDeclaredScale());
    }

    [Fact]
    public void GetDeclaredPrecision_PassthroughForNonDecimal()
    {
        var dataType = DataType.Float;

        Assert.Equal(dataType.NumericPrecision, dataType.GetDeclaredPrecision());
        Assert.Equal(dataType.NumericScale, dataType.GetDeclaredScale());
    }
}
