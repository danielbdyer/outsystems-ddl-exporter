using System;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo.TypeMapping;
using Xunit;

namespace Osm.Smo.Tests;

public sealed class TypeMappingPolicyTests
{
    private static readonly TypeMappingPolicy Policy = TypeMappingPolicy.LoadDefault();

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

        var result = Policy.Resolve(attribute);

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

        var result = Policy.Resolve(attribute);
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

        var result = Policy.Resolve(attribute);
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

        var result = Policy.Resolve(attribute);

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

        var result = Policy.Resolve(attribute);

        Assert.Equal(expected, result.SqlDataType);
    }

    [Fact]
    public void Resolve_MapsBinaryDataToVarBinary()
    {
        var attribute = CreateAttribute(dataType: "rtBinaryData", length: null);

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarBinaryMax, result.SqlDataType);
        Assert.True(result.MaximumLength <= 0);
    }

    [Fact]
    public void Resolve_MapsCurrencyToDecimalWithDefaultScale()
    {
        var attribute = CreateAttribute(dataType: "rtCurrency", length: null);

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.Decimal, result.SqlDataType);
        // SMO's Decimal stores the requested precision in NumericScale and the scale in NumericPrecision.
        Assert.Equal(37, result.NumericScale);
        Assert.Equal(8, result.NumericPrecision);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(2001)]
    public void Resolve_MapsTextualRuntimeTypesWithLength(int declaredLength)
    {
        var attribute = CreateAttribute(dataType: "rtText", length: declaredLength);

        var result = Policy.Resolve(attribute);

        if (declaredLength > 2000)
        {
            Assert.Equal(SqlDataType.NVarCharMax, result.SqlDataType);
        }
        else
        {
            Assert.Equal(SqlDataType.NVarChar, result.SqlDataType);
            Assert.Equal(declaredLength, result.MaximumLength);
        }
    }

    [Fact]
    public void Resolve_MapsEmailToVarChar()
    {
        var attribute = CreateAttribute(dataType: "rtEmail", length: null);

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarChar, result.SqlDataType);
        Assert.Equal(250, result.MaximumLength);
    }

    [Theory]
    [InlineData("rtPhoneNumber")]
    [InlineData("rtPhone")]
    public void Resolve_MapsPhoneToVarChar(string runtimeType)
    {
        var attribute = CreateAttribute(dataType: runtimeType, length: null);

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarChar, result.SqlDataType);
        Assert.Equal(20, result.MaximumLength);
    }

    [Fact]
    public void Resolve_UsesExternalDatabaseTypeForUnicode()
    {
        var attribute = CreateAttribute(dataType: "Text", length: null, externalDatabaseType: "NVARCHAR(128)");

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, result.SqlDataType);
        Assert.Equal(128, result.MaximumLength);
    }

    [Fact]
    public void Resolve_UsesExternalDatabaseTypeForMaxUnicode()
    {
        var attribute = CreateAttribute(dataType: "Text", length: null, externalDatabaseType: "NVARCHAR(MAX)");

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarCharMax, result.SqlDataType);
        Assert.True(result.MaximumLength <= 0);
    }

    [Fact]
    public void Resolve_UsesIdentifierMappingForPrimaryKeys()
    {
        var attribute = CreateAttribute(
            dataType: "Identifier",
            isIdentifier: true,
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: false,
                sqlType: "int",
                maxLength: null,
                precision: 10,
                scale: 0,
                collation: null,
                isIdentity: true,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null));

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.BigInt, result.SqlDataType);
    }

    [Fact]
    public void Resolve_UsesIdentifierMappingForForeignKeys()
    {
        var referenceResult = AttributeReference.Create(
            isReference: true,
            targetEntityId: null,
            targetEntity: new EntityName("Customer"),
            targetPhysicalName: new TableName("dbo.Customer"),
            deleteRuleCode: "Protect",
            hasDatabaseConstraint: true);
        Assert.True(referenceResult.IsSuccess);

        var attribute = CreateAttribute(
            dataType: "Identifier",
            reference: referenceResult.Value);

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.BigInt, result.SqlDataType);
    }

    [Fact]
    public void Resolve_PrefersIdentifierRuleOverOnDiskForForeignKeys()
    {
        var referenceResult = AttributeReference.Create(
            isReference: true,
            targetEntityId: 42,
            targetEntity: new EntityName("City"),
            targetPhysicalName: new TableName("dbo.OSUSR_DEF_CITY"),
            deleteRuleCode: "Protect",
            hasDatabaseConstraint: true);
        Assert.True(referenceResult.IsSuccess);

        var attribute = CreateAttribute(
            dataType: "Identifier",
            reference: referenceResult.Value,
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: false,
                sqlType: "int",
                maxLength: null,
                precision: 10,
                scale: 0,
                collation: null,
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null));

        var result = Policy.Resolve(attribute);

        Assert.Equal(SqlDataType.BigInt, result.SqlDataType);
    }

    private static AttributeModel CreateAttribute(
        string dataType = "Text",
        int? length = 50,
        int? precision = null,
        int? scale = null,
        string? externalDatabaseType = null,
        AttributeOnDiskMetadata? onDisk = null,
        bool isIdentifier = false,
        bool isAutoNumber = false,
        bool isActive = true,
        AttributeReference? reference = null)
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
            isIdentifier,
            isAutoNumber,
            isActive,
            reference ?? AttributeReference.None,
            externalDatabaseType,
            AttributeReality.Unknown,
            AttributeMetadata.Empty,
            onDisk ?? AttributeOnDiskMetadata.Empty);
}
