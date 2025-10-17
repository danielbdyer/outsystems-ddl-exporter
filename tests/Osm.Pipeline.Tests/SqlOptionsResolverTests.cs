using System;
using System.Collections.Generic;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class SqlOptionsResolverTests
{
    [Fact]
    public void Resolve_MergesOptionalColumnsFromOverrides()
    {
        var configuration = CliConfiguration.Empty with
        {
            Sql = new SqlConfiguration(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: SqlSamplingConfiguration.Empty,
                Authentication: SqlAuthenticationConfiguration.Empty,
                MetadataContract: new MetadataContractConfiguration(
                    new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["AttributeJson"] = new[] { "AttributesJson" },
                    })),
        };

        var overrides = new SqlOptionsOverrides(
            ConnectionString: null,
            CommandTimeoutSeconds: null,
            SamplingThreshold: null,
            SamplingSize: null,
            AuthenticationMethod: null,
            TrustServerCertificate: null,
            ApplicationName: null,
            AccessToken: null,
            OptionalColumns: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["AttributeJson"] = new[] { "RelationshipJson" },
                ["ForeignKeyAttributeJson"] = new[] { "AttributeJson" },
            });

        var result = SqlOptionsResolver.Resolve(configuration, overrides);

        Assert.True(result.IsSuccess);
        var optionalColumns = result.Value.MetadataContract.OptionalColumns;
        Assert.Equal(2, optionalColumns.Count);
        Assert.True(optionalColumns.TryGetValue("AttributeJson", out var attributeColumns));
        Assert.Contains("AttributesJson", attributeColumns);
        Assert.Contains("RelationshipJson", attributeColumns);
        Assert.True(optionalColumns.TryGetValue("ForeignKeyAttributeJson", out var foreignKeyColumns));
        var foreignKeyColumn = Assert.Single(foreignKeyColumns);
        Assert.Equal("AttributeJson", foreignKeyColumn);
    }
}
