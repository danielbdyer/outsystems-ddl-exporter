using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Xunit;

namespace Osm.Smo.Tests;

public sealed class SqlDataTypeMapperTests
{
    [Fact]
    public void Resolve_UsesOnDiskUnicodeLength()
    {
        var attribute = CreateAttribute(AttributeOnDiskMetadata.Create(
            isNullable: true,
            sqlType: "nvarchar",
            maxLength: 120,
            precision: null,
            scale: null,
            collation: null,
            isIdentity: false,
            isComputed: false,
            computedDefinition: null,
            defaultDefinition: null));

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, result.SqlDataType);
        Assert.Equal(120, result.MaximumLength);
    }

    [Fact]
    public void Resolve_UsesMaxForUnicodeWhenLengthIsMinusOne()
    {
        var attribute = CreateAttribute(AttributeOnDiskMetadata.Create(
            isNullable: true,
            sqlType: "nvarchar",
            maxLength: -1,
            precision: null,
            scale: null,
            collation: null,
            isIdentity: false,
            isComputed: false,
            computedDefinition: null,
            defaultDefinition: null));

        var result = SqlDataTypeMapper.Resolve(attribute);
        Assert.Equal(SqlDataType.NVarCharMax, result.SqlDataType);
        Assert.True(result.MaximumLength <= 0);
    }

    [Fact]
    public void Resolve_UsesDecimalPrecisionAndScaleFromOnDiskMetadata()
    {
        var attribute = CreateAttribute(AttributeOnDiskMetadata.Create(
            isNullable: true,
            sqlType: "decimal",
            maxLength: null,
            precision: 12,
            scale: 4,
            collation: null,
            isIdentity: false,
            isComputed: false,
            computedDefinition: null,
            defaultDefinition: null));

        var result = SqlDataTypeMapper.Resolve(attribute);
        Assert.Equal(SqlDataType.Decimal, result.SqlDataType);
        Assert.Equal(4, result.NumericPrecision);
        Assert.Equal(12, result.NumericScale);
    }

    [Fact]
    public void Resolve_FallsBackToAttributeLengthWhenOnDiskMissing()
    {
        var attribute = CreateAttribute(AttributeOnDiskMetadata.Create(
            isNullable: true,
            sqlType: "nvarchar",
            maxLength: null,
            precision: null,
            scale: null,
            collation: null,
            isIdentity: false,
            isComputed: false,
            computedDefinition: null,
            defaultDefinition: null));

        attribute = attribute with { Length = 80 };

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, result.SqlDataType);
        Assert.Equal(80, result.MaximumLength);
    }

    private static AttributeModel CreateAttribute(AttributeOnDiskMetadata onDisk)
        => new(
            new AttributeName("Attr"),
            new ColumnName("Attr"),
            null,
            "Text",
            50,
            null,
            null,
            null,
            false,
            false,
            false,
            true,
            AttributeReference.None,
            null,
            AttributeReality.Unknown,
            AttributeMetadata.Empty,
            onDisk);
}
