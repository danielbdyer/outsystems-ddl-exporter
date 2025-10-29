using System.Collections.Generic;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests.TypeMapping;

public sealed class TypeMappingPolicyLoaderTests
{
    [Fact]
    public void LoadDefault_AppliesDefaultOverride()
    {
        var defaultOverride = new TypeMappingRuleDefinition(
            TypeMappingStrategy.VarChar,
            SqlType: null,
            FallbackLength: 200,
            DefaultPrecision: null,
            DefaultScale: null,
            Scale: null,
            MaxLengthThreshold: null);

        var policy = TypeMappingPolicyLoader.LoadDefault(defaultOverride: defaultOverride);
        var attribute = CreateAttribute("CustomType", length: 64);

        var result = policy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarChar, result.SqlDataType);
        Assert.Equal(64, result.MaximumLength);
    }

    [Fact]
    public void LoadDefault_WithOverrides_ReplacesAttributeMapping()
    {
        var overrides = new Dictionary<string, TypeMappingRuleDefinition>
        {
            ["Text"] = new TypeMappingRuleDefinition(
                TypeMappingStrategy.VarChar,
                SqlType: null,
                FallbackLength: 80,
                DefaultPrecision: null,
                DefaultScale: null,
                Scale: null,
                MaxLengthThreshold: null)
        };

        var policy = TypeMappingPolicyLoader.LoadDefault(overrides: overrides);
        var attribute = CreateAttribute("Text", length: 40);

        var result = policy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarChar, result.SqlDataType);
        Assert.Equal(40, result.MaximumLength);
    }

    private static AttributeModel CreateAttribute(string dataType, int? length)
        => new(
            new AttributeName("Attr"),
            new ColumnName("Attr"),
            null,
            dataType,
            length,
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
            AttributeOnDiskMetadata.Empty);
}
