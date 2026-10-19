using System;
using Microsoft.SqlServer.Management.Smo;

namespace Osm.Smo;

public static class SmoDataTypeExtensions
{
    public static int GetDeclaredPrecision(this DataType dataType)
    {
        if (dataType is null)
        {
            throw new ArgumentNullException(nameof(dataType));
        }

        return dataType.SqlDataType switch
        {
            SqlDataType.Decimal or SqlDataType.Numeric => dataType.NumericScale,
            _ => dataType.NumericPrecision,
        };
    }

    public static int GetDeclaredScale(this DataType dataType)
    {
        if (dataType is null)
        {
            throw new ArgumentNullException(nameof(dataType));
        }

        return dataType.SqlDataType switch
        {
            SqlDataType.Decimal or SqlDataType.Numeric => dataType.NumericPrecision,
            _ => dataType.NumericScale,
        };
    }
}
