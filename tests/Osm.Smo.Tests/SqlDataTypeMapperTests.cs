using System;
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
        var attribute = CreateAttribute(
            onDisk: AttributeOnDiskMetadata.Create(
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
        var attribute = CreateAttribute(
            onDisk: AttributeOnDiskMetadata.Create(
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
        var attribute = CreateAttribute(
            onDisk: AttributeOnDiskMetadata.Create(
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
        var attribute = CreateAttribute(
            onDisk: AttributeOnDiskMetadata.Create(
            isNullable: true,
            sqlType: "nvarchar",
            maxLength: null,
            precision: null,
            scale: null,
            collation: null,
            isIdentity: false,
            isComputed: false,
            computedDefinition: null,
            defaultDefinition: null),
            length: 80);

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, result.SqlDataType);
        Assert.Equal(80, result.MaximumLength);
    }

    [Theory]
    [InlineData("rtInteger", SqlDataType.Int)]
    [InlineData("Integer", SqlDataType.Int)]
    [InlineData("rtLongInteger", SqlDataType.BigInt)]
    [InlineData("rtBoolean", SqlDataType.Bit)]
    [InlineData("rtDateTime", SqlDataType.DateTime)]
    [InlineData("rtDate", SqlDataType.Date)]
    public void Resolve_MapsOutSystemsRuntimeTypes(string runtimeType, SqlDataType expected)
    {
        var attribute = CreateAttribute(dataType: runtimeType, length: 50);

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(expected, result.SqlDataType);
    }

    [Fact]
    public void Resolve_MapsBinaryDataToVarBinary()
    {
        var attribute = CreateAttribute(dataType: "rtBinaryData", length: null);

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.VarBinaryMax, result.SqlDataType);
        Assert.True(result.MaximumLength <= 0);
    }

    [Fact]
    public void Resolve_MapsCurrencyToDecimalWithDefaultScale()
    {
        var attribute = CreateAttribute(dataType: "rtCurrency", length: null);

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.Decimal, result.SqlDataType);
        Assert.Equal(19, result.NumericPrecision);
        Assert.Equal(4, result.NumericScale);
    }

    [Theory]
    [InlineData("rtText", 200)]
    [InlineData("rtEmail", 254)]
    [InlineData("rtPhoneNumber", 50)]
    public void Resolve_MapsTextualRuntimeTypesWithLength(string runtimeType, int expectedLength)
    {
        var attribute = CreateAttribute(dataType: runtimeType, length: runtimeType.Equals("rtText", StringComparison.OrdinalIgnoreCase) ? 200 : null);

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, result.SqlDataType);
        Assert.Equal(expectedLength, result.MaximumLength);
    }

    [Fact]
    public void Resolve_UsesExternalDatabaseTypeForUnicode()
    {
        var attribute = CreateAttribute(dataType: "Text", length: null, externalDatabaseType: "NVARCHAR(128)");

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, result.SqlDataType);
        Assert.Equal(128, result.MaximumLength);
    }

    [Fact]
    public void Resolve_UsesExternalDatabaseTypeForMaxUnicode()
    {
        var attribute = CreateAttribute(dataType: "Text", length: null, externalDatabaseType: "NVARCHAR(MAX)");

        var result = SqlDataTypeMapper.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarCharMax, result.SqlDataType);
        Assert.True(result.MaximumLength <= 0);
    }

    private static AttributeModel CreateAttribute(
        string dataType = "Text",
        int? length = 50,
        int? precision = null,
        int? scale = null,
        string? externalDatabaseType = null,
        AttributeOnDiskMetadata? onDisk = null)
        => new(
            new AttributeName("Attr"),
            new ColumnName("Attr"),
            null,
            dataType,
            length,
            precision,
            scale,
            null,
            false,
            false,
            false,
            true,
            AttributeReference.None,
            externalDatabaseType,
            AttributeReality.Unknown,
            AttributeMetadata.Empty,
            onDisk ?? AttributeOnDiskMetadata.Empty);
}
