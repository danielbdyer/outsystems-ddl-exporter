using System.IO;
using Osm.App.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Smo.TypeMapping;
using Tests.Support;

namespace Osm.Cli.Tests.Configuration;

public sealed class TypeMappingPolicyResolverTests
{
    [Fact]
    public void Resolve_ReturnsDefault_WhenNoOverride()
    {
        var configuration = CliConfiguration.Empty;

        var result = TypeMappingPolicyResolver.Resolve(configuration, null);

        Assert.True(result.IsSuccess);
        var attribute = CreateAttribute("Email", dataType: "Email");
        var dataType = result.Value.Resolve(attribute);
        Assert.Equal("varchar", dataType.SqlDataType.ToString().ToLowerInvariant());
    }

    [Fact]
    public void Resolve_ReturnsError_WhenOverrideMissing()
    {
        var configuration = CliConfiguration.Empty with
        {
            TypeMapping = new TypeMappingConfiguration("missing.json")
        };

        var result = TypeMappingPolicyResolver.Resolve(configuration, null);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "cli.config.typemap.missing");
    }

    [Fact]
    public void Resolve_MergesOverride()
    {
        using var directory = new TempDirectory();
        var overridePath = Path.Combine(directory.Path, "type-map.json");
        File.WriteAllText(overridePath, "{\"rules\":[{\"logicalType\":\"email\",\"kind\":\"unicode\",\"maxLength\":400}]}" );

        var configuration = CliConfiguration.Empty with
        {
            TypeMapping = new TypeMappingConfiguration(overridePath)
        };

        var result = TypeMappingPolicyResolver.Resolve(configuration, null);

        Assert.True(result.IsSuccess);
        var attribute = CreateAttribute("EmailOverride", dataType: "Email");
        var dataType = result.Value.Resolve(attribute);
        Assert.Equal("nvarchar", dataType.SqlDataType.ToString().ToLowerInvariant());
        Assert.Equal(400, dataType.MaximumLength);
    }

    private static AttributeModel CreateAttribute(string logicalName, string dataType)
    {
        return new AttributeModel(
            new AttributeName(logicalName),
            new ColumnName(logicalName),
            null,
            dataType,
            null,
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
}
