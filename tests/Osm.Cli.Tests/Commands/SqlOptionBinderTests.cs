using System;
using System.CommandLine;
using Microsoft.Data.SqlClient;
using Osm.Cli.Commands.Binders;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class SqlOptionBinderTests
{
    [Fact]
    public void GetValue_ParsesSqlOptions()
    {
        var binder = new SqlOptionBinder();
        var command = new Command("test");
        foreach (var option in binder.Options)
        {
            command.AddOption(option);
        }

        var parseResult = command.Parse(
            "--connection-string DataSource " +
            "--command-timeout 120 " +
            "--sampling-threshold 500 " +
            "--sampling-size 25 " +
            "--sql-authentication ActiveDirectoryIntegrated " +
            "--sql-trust-server-certificate false " +
            "--sql-application-name osm " +
            "--sql-access-token token " +
            "--sql-optional-column AttributeJson:AttributesJson " +
            "--sql-optional-column AttributeJson:RelationshipJson " +
            "--sql-optional-column ForeignKeyAttributeJson:AttributeJson");

        var overrides = binder.Bind(parseResult);

        Assert.Equal("DataSource", overrides.ConnectionString);
        Assert.Equal(120, overrides.CommandTimeoutSeconds);
        Assert.Equal(500, overrides.SamplingThreshold);
        Assert.Equal(25, overrides.SamplingSize);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryIntegrated, overrides.AuthenticationMethod);
        Assert.False(overrides.TrustServerCertificate);
        Assert.Equal("osm", overrides.ApplicationName);
        Assert.Equal("token", overrides.AccessToken);
        var optionalColumns = overrides.OptionalColumns;
        Assert.NotNull(optionalColumns);
        var columnDictionary = optionalColumns!;
        Assert.Equal(2, columnDictionary.Count);
        Assert.True(columnDictionary.TryGetValue("AttributeJson", out var attributeColumns));
        Assert.Collection(
            attributeColumns,
            column => Assert.Equal("AttributesJson", column, StringComparer.OrdinalIgnoreCase),
            column => Assert.Equal("RelationshipJson", column, StringComparer.OrdinalIgnoreCase));
        Assert.True(columnDictionary.TryGetValue("ForeignKeyAttributeJson", out var foreignKeyColumns));
        var foreignKeyColumn = Assert.Single(foreignKeyColumns);
        Assert.Equal("AttributeJson", foreignKeyColumn, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetValue_DefaultsTrustServerCertificateWhenFlagProvidedWithoutValue()
    {
        var binder = new SqlOptionBinder();
        var command = new Command("test");
        foreach (var option in binder.Options)
        {
            command.AddOption(option);
        }

        var parseResult = command.Parse("--sql-trust-server-certificate");

        var overrides = binder.Bind(parseResult);

        Assert.True(overrides.TrustServerCertificate);
    }

    [Fact]
    public void GetValue_RejectsInvalidOptionalColumnFormat()
    {
        var binder = new SqlOptionBinder();
        var command = new Command("test");
        foreach (var option in binder.Options)
        {
            command.AddOption(option);
        }

        var parseResult = command.Parse("--sql-optional-column InvalidValue");

        Assert.NotEmpty(parseResult.Errors);
        Assert.Contains(parseResult.Errors, error => error.Message.Contains("ResultSet:Column", StringComparison.Ordinal));
    }
}
