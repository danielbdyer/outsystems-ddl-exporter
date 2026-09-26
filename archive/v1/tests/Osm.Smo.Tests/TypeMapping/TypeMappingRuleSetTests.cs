using System;
using System.IO;
using System.Text;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests.TypeMapping;

public sealed class TypeMappingRuleSetTests
{
    [Fact]
    public void UnicodeTextRules_HonorOnDiskDefaultsAndOverrides()
    {
        var attribute = CreateAttribute(
            dataType: "Text",
            length: null,
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "nvarchar",
                maxLength: 150,
                precision: null,
                scale: null,
                collation: null,
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null));

        var defaultPolicy = TypeMappingPolicyLoader.LoadDefault();
        var defaultResult = defaultPolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, defaultResult.SqlDataType);
        Assert.Equal(150, defaultResult.MaximumLength);

        var overridePolicy = LoadCustomPolicy(
            """
            {
              "default": {
                "strategy": "UnicodeText",
                "maxLengthThreshold": 2000
              },
              "mappings": {},
              "onDisk": {
                "nvarchar": {
                  "strategy": "UnicodeText",
                  "lengthSource": "OnDiskOrAttribute",
                  "maxLengthThreshold": 100
                }
              },
              "external": {}
            }
            """);

        var overrideResult = overridePolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarCharMax, overrideResult.SqlDataType);
        Assert.True(overrideResult.MaximumLength <= 0);
    }

    [Fact]
    public void NumericRules_DefaultToFallbackPrecisionAndRespectOverrides()
    {
        var attribute = CreateAttribute(
            dataType: "Decimal",
            precision: null,
            scale: null,
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "decimal",
                maxLength: null,
                precision: null,
                scale: null,
                collation: null,
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null));

        var defaultPolicy = TypeMappingPolicyLoader.LoadDefault();
        var defaultResult = defaultPolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.Decimal, defaultResult.SqlDataType);
        Assert.Equal(18, defaultResult.GetDeclaredPrecision());
        Assert.Equal(0, defaultResult.GetDeclaredScale());

        var overridePolicy = LoadCustomPolicy(
            """
            {
              "default": "nvarchar(max)",
              "mappings": {},
              "onDisk": {
                "decimal": {
                  "strategy": "Decimal",
                  "precisionSource": "OnDiskOrAttribute",
                  "scaleSource": "OnDiskOrAttribute",
                  "defaultPrecision": 30,
                  "defaultScale": 4
                }
              },
              "external": {}
            }
            """);

        var overrideResult = overridePolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.Decimal, overrideResult.SqlDataType);
        Assert.Equal(30, overrideResult.GetDeclaredPrecision());
        Assert.Equal(4, overrideResult.GetDeclaredScale());
    }

    [Fact]
    public void BinaryRules_ApplyThresholdOverrides()
    {
        var attribute = CreateAttribute(
            dataType: "BinaryData",
            length: null,
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "varbinary",
                maxLength: 4096,
                precision: null,
                scale: null,
                collation: null,
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null));

        var defaultPolicy = TypeMappingPolicyLoader.LoadDefault();
        var defaultResult = defaultPolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarBinaryMax, defaultResult.SqlDataType);
        Assert.True(defaultResult.MaximumLength <= 0);

        var overridePolicy = LoadCustomPolicy(
            """
            {
              "default": "nvarchar(max)",
              "mappings": {},
              "onDisk": {
                "varbinary": {
                  "strategy": "VarBinary",
                  "lengthSource": "OnDiskOrAttribute",
                  "maxLengthThreshold": 5000
                }
              },
              "external": {}
            }
            """);

        var overrideResult = overridePolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarBinary, overrideResult.SqlDataType);
        Assert.Equal(4096, overrideResult.MaximumLength);
    }

    [Fact]
    public void ExternalRules_ParseParametersAndHonorOverrides()
    {
        var attribute = CreateAttribute(
            dataType: "Text",
            length: null,
            externalDatabaseType: "NVARCHAR(128)");

        var defaultPolicy = TypeMappingPolicyLoader.LoadDefault();
        var defaultResult = defaultPolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarChar, defaultResult.SqlDataType);
        Assert.Equal(128, defaultResult.MaximumLength);

        var overridePolicy = LoadCustomPolicy(
            """
            {
              "default": "nvarchar(max)",
              "mappings": {},
              "onDisk": {},
              "external": {
                "nvarchar": {
                  "strategy": "UnicodeText",
                  "lengthSource": "Parameters",
                  "lengthParameterIndex": 0,
                  "maxLengthThreshold": 100
                }
              }
            }
            """);

        var overrideResult = overridePolicy.Resolve(attribute);

        Assert.Equal(SqlDataType.NVarCharMax, overrideResult.SqlDataType);
        Assert.True(overrideResult.MaximumLength <= 0);
    }

    private static TypeMappingPolicy LoadCustomPolicy(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes);
        return TypeMappingPolicyLoader.Load(stream, null, null);
    }

    private static AttributeModel CreateAttribute(
        string dataType,
        int? length = null,
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
