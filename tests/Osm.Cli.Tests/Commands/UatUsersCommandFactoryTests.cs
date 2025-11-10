using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Pipeline.Configuration;
using Xunit;
using Tests.Support;

namespace Osm.Cli.Tests.Commands;

public class UatUsersCommandFactoryTests
{
    [Fact]
    public async Task Invoke_ParsesOptions()
    {
        var executor = new FakeUatUsersCommand();

        await using var provider = CreateServiceProvider(executor);
        var factory = provider.GetRequiredService<UatUsersCommandFactory>();
        var command = factory.Create();
        Assert.NotNull(command.Handler);
        Assert.Contains(command.Options, option => string.Equals(option.Name, "user-map", StringComparison.Ordinal));

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var args = "uat-users --model model.json --uat-conn Server=.;Database=UAT; --user-schema dbo --user-table dbo.Users --user-id-column UserId --include-columns Name --include-columns EMail --out artifacts --user-map map.csv --user-ddl ddl.sql --snapshot snap.json --user-entity-id Identifier";
        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(5, exitCode);
        var options = executor.LastOptions!;
        Assert.Equal(Path.GetFullPath("model.json"), options.ModelPath);
        Assert.Equal("Server=.;Database=UAT;", options.UatConnectionString);
        Assert.False(options.FromLiveMetadata);
        Assert.Equal("dbo", options.UserSchema);
        Assert.Equal("Users", options.UserTable);
        Assert.Equal("UserId", options.UserIdColumn);
        Assert.Equal(new[] { "Name", "EMail" }, options.IncludeColumns);
        Assert.Equal(Path.GetFullPath("artifacts"), options.OutputDirectory);
        Assert.Equal(Path.GetFullPath("map.csv"), options.UserMapPath);
        Assert.Equal(Path.GetFullPath("ddl.sql"), options.AllowedUsersSqlPath);
        Assert.Equal(Path.GetFullPath("snap.json"), options.SnapshotPath);
        Assert.Equal("Identifier", options.UserEntityIdentifier);
        Assert.False(options.Origins.ModelPathFromConfiguration);
        Assert.False(options.Origins.ConnectionStringFromConfiguration);
    }

    [Theory]
    [InlineData("\"[dbo].[Users]\"", "dbo", "Users")]
    [InlineData("\"[custom].[User Accounts]\"", "custom", "User Accounts")]
    [InlineData("\"schema.[User.Table]\"", "schema", "User.Table")]
    [InlineData("\"[custom schema].User\"", "custom schema", "User")]
    public async Task Invoke_NormalizesBracketedUserTableInput(string userTableArgument, string expectedSchema, string expectedTable)
    {
        var command = $"uat-users --model model.json --uat-conn Server=.;Database=UAT; --user-ddl ddl.sql --user-table {userTableArgument}";
        var (options, exitCode) = await InvokeAsync(command);

        Assert.Equal(5, exitCode);
        Assert.Equal(expectedSchema, options.UserSchema);
        Assert.Equal(expectedTable, options.UserTable);
    }

    [Fact]
    public async Task Invoke_DeduplicatesIncludeColumnsIgnoringCase()
    {
        var command = "uat-users --model model.json --uat-conn Server=.;Database=UAT; --user-ddl ddl.sql --include-columns Name --include-columns name --include-columns EMail --include-columns EMAIL";
        var (options, exitCode) = await InvokeAsync(command);

        Assert.Equal(5, exitCode);
        Assert.Equal(new[] { "Name", "EMail" }, options.IncludeColumns);
    }

    private static async Task<(UatUsersOptions Options, int ExitCode)> InvokeAsync(string commandLine)
    {
        var executor = new FakeUatUsersCommand();

        await using var provider = CreateServiceProvider(executor, configuration);
        var factory = provider.GetRequiredService<UatUsersCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var exitCode = await parser.InvokeAsync(commandLine);

        Assert.NotNull(executor.LastOptions);
        return (executor.LastOptions!, exitCode);
    }

    [Fact]
    public async Task Invoke_UsesConfigurationDefaults_WhenOptionsOmitted()
    {
        using var temp = new TempDirectory();
        var modelPath = Path.Combine(temp.Path, "model.json");
        var outputRoot = Path.Combine(temp.Path, "artifacts");
        var userMapPath = Path.Combine(temp.Path, "map.csv");
        var allowedSqlPath = Path.Combine(temp.Path, "allowed.sql");
        var allowedIdsPath = Path.Combine(temp.Path, "allowed.csv");
        var snapshotPath = Path.Combine(temp.Path, "snapshot.json");

        var configuration = CliConfiguration.Empty with
        {
            UatUsers = new UatUsersConfiguration(
                ModelPath: modelPath,
                ConnectionString: "Server=.;Database=UAT;",
                FromLiveMetadata: false,
                UserSchema: "app",
                UserTable: "dbo.Users",
                UserIdColumn: "UserId",
                IncludeColumns: new[] { "CreatedBy", "UpdatedBy" },
                OutputRoot: outputRoot,
                UserMapPath: userMapPath,
                AllowedUsersSqlPath: allowedSqlPath,
                AllowedUserIdsPath: allowedIdsPath,
                SnapshotPath: snapshotPath,
                UserEntityIdentifier: "UserEntity")
        };

        var (options, exitCode) = await InvokeAsync("uat-users", configuration);

        Assert.Equal(5, exitCode);
        Assert.Equal(Path.GetFullPath(modelPath), options.ModelPath);
        Assert.Equal("Server=.;Database=UAT;", options.UatConnectionString);
        Assert.False(options.FromLiveMetadata);
        Assert.Equal("dbo", options.UserSchema);
        Assert.Equal("Users", options.UserTable);
        Assert.Equal("UserId", options.UserIdColumn);
        Assert.Equal(new[] { "CreatedBy", "UpdatedBy" }, options.IncludeColumns);
        Assert.Equal(Path.GetFullPath(outputRoot), options.OutputDirectory);
        Assert.Equal(Path.GetFullPath(userMapPath), options.UserMapPath);
        Assert.Equal(Path.GetFullPath(allowedSqlPath), options.AllowedUsersSqlPath);
        Assert.Equal(Path.GetFullPath(allowedIdsPath), options.AllowedUserIdsPath);
        Assert.Equal(Path.GetFullPath(snapshotPath), options.SnapshotPath);
        Assert.Equal("UserEntity", options.UserEntityIdentifier);

        Assert.True(options.Origins.ModelPathFromConfiguration);
        Assert.True(options.Origins.ConnectionStringFromConfiguration);
        Assert.True(options.Origins.UserTableFromConfiguration);
        Assert.True(options.Origins.UserSchemaFromConfiguration);
        Assert.True(options.Origins.UserIdColumnFromConfiguration);
        Assert.True(options.Origins.IncludeColumnsFromConfiguration);
        Assert.True(options.Origins.OutputDirectoryFromConfiguration);
        Assert.True(options.Origins.UserMapPathFromConfiguration);
        Assert.True(options.Origins.AllowedUsersSqlPathFromConfiguration);
        Assert.True(options.Origins.AllowedUserIdsPathFromConfiguration);
        Assert.True(options.Origins.SnapshotPathFromConfiguration);
        Assert.True(options.Origins.UserEntityIdentifierFromConfiguration);
    }

    private sealed class FakeUatUsersCommand : IUatUsersCommand
    {
        public UatUsersOptions? LastOptions { get; private set; }

        public Task<int> ExecuteAsync(UatUsersOptions options, CancellationToken cancellationToken)
        {
            LastOptions = options;
            return Task.FromResult(5);
        }
    }

    private static ServiceProvider CreateServiceProvider(FakeUatUsersCommand executor, CliConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUatUsersCommand>(executor);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ICliConfigurationService>(new StubConfigurationService(configuration ?? CliConfiguration.Empty));
        services.AddSingleton<UatUsersCommandFactory>();
        return services.BuildServiceProvider();
    }

    private sealed class StubConfigurationService : ICliConfigurationService
    {
        private readonly CliConfiguration _configuration;

        public StubConfigurationService(CliConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<Osm.Domain.Abstractions.Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            var context = new CliConfigurationContext(_configuration, overrideConfigPath);
            return Task.FromResult(Osm.Domain.Abstractions.Result<CliConfigurationContext>.Success(context));
        }
    }
}
