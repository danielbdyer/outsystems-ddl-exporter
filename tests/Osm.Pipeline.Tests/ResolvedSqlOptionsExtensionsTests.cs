using System;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class ResolvedSqlOptionsExtensionsTests
{
    [Fact]
    public void ToSamplingOptions_UsesDefaults_WhenSettingsAreNull()
    {
        var options = CreateOptions(sampling: new SqlSamplingSettings(null, null));

        var sampling = options.ToSamplingOptions();

        Assert.Equal(SqlSamplingOptions.Default.RowCountSamplingThreshold, sampling.RowCountSamplingThreshold);
        Assert.Equal(SqlSamplingOptions.Default.SampleSize, sampling.SampleSize);
    }

    [Fact]
    public void ToSamplingOptions_PreservesExplicitValues()
    {
        var options = CreateOptions(sampling: new SqlSamplingSettings(42, 128));

        var sampling = options.ToSamplingOptions();

        Assert.Equal(42, sampling.RowCountSamplingThreshold);
        Assert.Equal(128, sampling.SampleSize);
    }

    [Fact]
    public void ToConnectionOptions_UsesDefaults_WhenAuthenticationIsUnset()
    {
        var options = CreateOptions(authentication: new SqlAuthenticationSettings(null, null, null, null));

        var connection = options.ToConnectionOptions();

        Assert.Equal(SqlConnectionOptions.Default, connection);
    }

    [Fact]
    public void ToConnectionOptions_PreservesAuthenticationSettings()
    {
        var options = CreateOptions(authentication: new SqlAuthenticationSettings(
            SqlAuthenticationMethod.ActiveDirectoryInteractive,
            true,
            "app",
            "token"));

        var connection = options.ToConnectionOptions();

        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryInteractive, connection.AuthenticationMethod);
        Assert.True(connection.TrustServerCertificate);
        Assert.Equal("app", connection.ApplicationName);
        Assert.Equal("token", connection.AccessToken);
    }

    [Fact]
    public void ToExecutionOptions_ComposesTimeoutAndSampling()
    {
        var options = CreateOptions(
            commandTimeoutSeconds: 30,
            sampling: new SqlSamplingSettings(10, 5));

        var execution = options.ToExecutionOptions();

        Assert.Equal(30, execution.CommandTimeoutSeconds);
        Assert.Equal(10, execution.Sampling.RowCountSamplingThreshold);
        Assert.Equal(5, execution.Sampling.SampleSize);
    }

    [Fact]
    public void ToExecutionOptions_Throws_WhenOptionsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => ResolvedSqlOptionsExtensions.ToExecutionOptions(null!));
    }

    private static ResolvedSqlOptions CreateOptions(
        SqlSamplingSettings? sampling = null,
        SqlAuthenticationSettings? authentication = null,
        int? commandTimeoutSeconds = null)
    {
        return new ResolvedSqlOptions(
            ConnectionString: "Server=(local);",
            CommandTimeoutSeconds: commandTimeoutSeconds,
            Sampling: sampling ?? new SqlSamplingSettings(null, null),
            Authentication: authentication ?? new SqlAuthenticationSettings(null, null, null, null));
    }
}
