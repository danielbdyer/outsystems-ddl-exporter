using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests.TypeMapping;

public sealed class TypeMappingPolicyLoaderTests
{
    [Fact]
    public void Load_AppliesAttributeOverrides()
    {
        const string json = """
        {
          "default": "nvarchar(max)",
          "mappings": {
            "text": {
              "strategy": "UnicodeText"
            }
          },
          "onDisk": {},
          "external": {}
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var overrides = new Dictionary<string, TypeMappingRuleDefinition>
        {
            ["text"] = new TypeMappingRuleDefinition(
                TypeMappingStrategy.VarChar,
                SqlType: "varchar",
                FallbackLength: 42,
                DefaultPrecision: null,
                DefaultScale: null,
                Scale: null,
                MaxLengthThreshold: null)
        };

        var policy = TypeMappingPolicyLoader.Load(stream, defaultOverride: null, overrides);
        var attribute = CreateAttribute("Text", length: null);

        var result = policy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarChar, result.SqlDataType);
        Assert.Equal(42, result.MaximumLength);
    }

    [Fact]
    public void Load_UsesDefaultOverrideWhenMappingMissing()
    {
        const string json = """
        {
          "default": "nvarchar(max)",
          "mappings": {},
          "onDisk": {},
          "external": {}
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var defaultOverride = new TypeMappingRuleDefinition(
            TypeMappingStrategy.VarChar,
            SqlType: "varchar",
            FallbackLength: 128,
            DefaultPrecision: null,
            DefaultScale: null,
            Scale: null,
            MaxLengthThreshold: null);

        var policy = TypeMappingPolicyLoader.Load(stream, defaultOverride, overrides: null);
        var attribute = CreateAttribute("Unmapped", length: null);

        var result = policy.Resolve(attribute);

        Assert.Equal(SqlDataType.VarChar, result.SqlDataType);
        Assert.Equal(128, result.MaximumLength);
    }

    private static AttributeModel CreateAttribute(string dataType, int? length)
        => new(
            new AttributeName("Attr"),
            new ColumnName("Attr"),
            null,
            dataType,
            length,
            Precision: null,
            Scale: null,
            DefaultValue: null,
            IsMandatory: false,
            IsIdentifier: false,
            IsAutoNumber: false,
            IsActive: true,
            AttributeReference.None,
            ExternalDatabaseType: null,
            AttributeReality.Unknown,
            AttributeMetadata.Empty,
            AttributeOnDiskMetadata.Empty);
}
