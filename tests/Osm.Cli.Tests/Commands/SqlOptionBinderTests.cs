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

        var parseResult = command.Parse("--connection-string DataSource --command-timeout 120 --sampling-threshold 500 --sampling-size 25 --sql-authentication ActiveDirectoryIntegrated --sql-trust-server-certificate false --sql-application-name osm --sql-access-token token");

        var overrides = binder.Bind(parseResult);

        Assert.Equal("DataSource", overrides.ConnectionString);
        Assert.Equal(120, overrides.CommandTimeoutSeconds);
        Assert.Equal(500, overrides.SamplingThreshold);
        Assert.Equal(25, overrides.SamplingSize);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryIntegrated, overrides.AuthenticationMethod);
        Assert.False(overrides.TrustServerCertificate);
        Assert.Equal("osm", overrides.ApplicationName);
        Assert.Equal("token", overrides.AccessToken);
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
}
