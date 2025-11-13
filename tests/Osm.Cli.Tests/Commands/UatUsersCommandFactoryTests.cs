using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.UatUsers;
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
        var args = "uat-users --model model.json --connection-string Server=.;Database=QA; --user-schema dbo --user-table dbo.Users --user-id-column UserId --include-columns Name --include-columns EMail --out artifacts --user-map map.csv --uat-user-inventory uat.csv --qa-user-inventory qa.csv --snapshot snap.json --user-entity-id Identifier --match-strategy regex --match-attribute Username --match-regex ^qa_(?<target>.*)$ --match-fallback-mode round-robin --match-fallback-target 200 --match-fallback-target 300 --idempotent-emission";
        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(5, exitCode);
        var options = executor.LastOptions!;
        Assert.Equal(Path.GetFullPath("model.json"), options.ModelPath);
        Assert.Equal("Server=.;Database=QA;", options.ConnectionString);
        Assert.False(options.FromLiveMetadata);
        Assert.Equal("dbo", options.UserSchema);
        Assert.Equal("User", options.UserTable);
        Assert.Equal("UserId", options.UserIdColumn);
        Assert.Equal(new[] { "Name", "EMail" }, options.IncludeColumns);
        Assert.Equal(Path.GetFullPath("artifacts"), options.OutputDirectory);
        Assert.Equal(Path.GetFullPath("map.csv"), options.UserMapPath);
        Assert.Equal(Path.GetFullPath("uat.csv"), options.UatUserInventoryPath);
        Assert.Equal(Path.GetFullPath("qa.csv"), options.QaUserInventoryPath);
        Assert.Equal(Path.GetFullPath("snap.json"), options.SnapshotPath);
        Assert.Equal("Identifier", options.UserEntityIdentifier);
        Assert.Equal(UserMatchingStrategy.Regex, options.MatchingStrategy);
        Assert.Equal("Username", options.MatchingAttribute);
        Assert.Equal("^qa_(?<target>.*)$", options.MatchingRegexPattern);
        Assert.Equal(UserFallbackAssignmentMode.RoundRobin, options.FallbackMode);
        Assert.Equal(new[] { "200", "300" }, options.FallbackTargets.Select(target => target.Value));
        Assert.True(options.IdempotentEmission);
        Assert.False(options.Origins.ModelPathFromConfiguration);
        Assert.False(options.Origins.ConnectionStringFromConfiguration);
        Assert.False(options.Origins.IdempotentEmissionFromConfiguration);
    }

    [Theory]
    [InlineData("\"[dbo].[Users]\"", "dbo", "Users")]
    [InlineData("\"[custom].[User Accounts]\"", "custom", "User Accounts")]
    [InlineData("\"schema.[User.Table]\"", "schema", "User.Table")]
    [InlineData("\"[custom schema].User\"", "custom schema", "User")]
    public async Task Invoke_NormalizesBracketedUserTableInput(string userTableArgument, string expectedSchema, string expectedTable)
    {
        var command = $"uat-users --model model.json --connection-string Server=.;Database=QA; --uat-user-inventory uat.csv --qa-user-inventory qa.csv --user-table {userTableArgument}";
        var (options, exitCode) = await InvokeAsync(command);

        Assert.Equal(5, exitCode);
        Assert.Equal(expectedSchema, options.UserSchema);
        Assert.Equal(expectedTable, options.UserTable);
    }

    [Fact]
    public async Task Invoke_DeduplicatesIncludeColumnsIgnoringCase()
    {
        var command = "uat-users --model model.json --connection-string Server=.;Database=QA; --uat-user-inventory uat.csv --qa-user-inventory qa.csv --include-columns Name --include-columns name --include-columns EMail --include-columns EMAIL";
        var (options, exitCode) = await InvokeAsync(command);

        Assert.Equal(5, exitCode);
        Assert.Equal(new[] { "Name", "EMail" }, options.IncludeColumns);
    }

    [Fact]
    public async Task Invoke_AcceptsUatInventoryCsv()
    {
        var command = "uat-users --model model.json --connection-string Server=.;Database=QA; --uat-user-inventory allowed.csv --qa-user-inventory qa.csv";
        var (options, exitCode) = await InvokeAsync(command);

        Assert.Equal(5, exitCode);
        Assert.Equal(Path.GetFullPath("allowed.csv"), options.UatUserInventoryPath);
    }

    [Fact]
    public async Task Invoke_RequiresQaInventory()
    {
        var executor = new FakeUatUsersCommand();

        await using var provider = CreateServiceProvider(executor);
        var factory = provider.GetRequiredService<UatUsersCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var args = "uat-users --model model.json --connection-string Server=.;Database=QA; --uat-user-inventory allowed.csv";
        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(1, exitCode);
        Assert.Contains("--qa-user-inventory is required", console.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(executor.LastOptions);
    }

    [Fact]
    public async Task Invoke_RequiresUatInventory()
    {
        var executor = new FakeUatUsersCommand();

        await using var provider = CreateServiceProvider(executor);
        var factory = provider.GetRequiredService<UatUsersCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var args = "uat-users --model model.json --connection-string Server=.;Database=QA; --qa-user-inventory qa.csv";
        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(1, exitCode);
        Assert.Contains("--uat-user-inventory is required", console.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(executor.LastOptions);
    }

    private static async Task<(UatUsersOptions Options, int ExitCode)> InvokeAsync(
        string commandLine,
        CliConfiguration? configuration = null)
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
        var uatInventoryPath = Path.Combine(temp.Path, "uat.csv");
        var snapshotPath = Path.Combine(temp.Path, "snapshot.json");
        var qaInventoryPath = Path.Combine(temp.Path, "qa.csv");

        var sqlConfiguration = new SqlConfiguration(
            ConnectionString: "Server=.;Database=QA;",
            CommandTimeoutSeconds: null,
            Sampling: SqlSamplingConfiguration.Empty,
            Authentication: SqlAuthenticationConfiguration.Empty,
            MetadataContract: MetadataContractConfiguration.Empty,
            ProfilingConnectionStrings: Array.Empty<string>(),
            TableNameMappings: Array.Empty<TableNameMappingConfiguration>());

        var configuration = CliConfiguration.Empty with
        {
            Sql = sqlConfiguration,
            UatUsers = new UatUsersConfiguration(
                ModelPath: modelPath,
                FromLiveMetadata: false,
                UserSchema: "dbo",
                UserTable: "Users",
                UserIdColumn: "UserId",
                IncludeColumns: new[] { "CreatedBy", "UpdatedBy" },
                OutputRoot: outputRoot,
                UserMapPath: userMapPath,
                UatUserInventoryPath: uatInventoryPath,
                QaUserInventoryPath: qaInventoryPath,
                SnapshotPath: snapshotPath,
                UserEntityIdentifier: "UserEntity",
                MatchingStrategy: UserMatchingStrategy.Regex,
                MatchingAttribute: "Username",
                MatchingRegexPattern: "^qa_(?<target>.*)$",
                FallbackAssignment: UserFallbackAssignmentMode.SingleTarget,
                FallbackTargets: new[] { "400" },
                IdempotentEmission: true)
        };

        var (options, exitCode) = await InvokeAsync("uat-users", configuration);

        Assert.Equal(5, exitCode);
        Assert.Equal(Path.GetFullPath(modelPath), options.ModelPath);
        Assert.Equal("Server=.;Database=QA;", options.ConnectionString);
        Assert.False(options.FromLiveMetadata);
        Assert.Equal("dbo", options.UserSchema);
        Assert.Equal("User", options.UserTable);
        Assert.Equal("UserId", options.UserIdColumn);
        Assert.Equal(new[] { "CreatedBy", "UpdatedBy" }, options.IncludeColumns);
        Assert.Equal(Path.GetFullPath(outputRoot), options.OutputDirectory);
        Assert.Equal(Path.GetFullPath(userMapPath), options.UserMapPath);
        Assert.Equal(Path.GetFullPath(uatInventoryPath), options.UatUserInventoryPath);
        Assert.Equal(Path.GetFullPath(qaInventoryPath), options.QaUserInventoryPath);
        Assert.Equal(Path.GetFullPath(snapshotPath), options.SnapshotPath);
        Assert.Equal("UserEntity", options.UserEntityIdentifier);
        Assert.Equal(UserMatchingStrategy.Regex, options.MatchingStrategy);
        Assert.Equal("Username", options.MatchingAttribute);
        Assert.Equal("^qa_(?<target>.*)$", options.MatchingRegexPattern);
        Assert.Equal(UserFallbackAssignmentMode.SingleTarget, options.FallbackMode);
        Assert.Equal(new[] { "400" }, options.FallbackTargets.Select(target => target.Value));
        Assert.True(options.IdempotentEmission);

        Assert.True(options.Origins.ModelPathFromConfiguration);
        Assert.True(options.Origins.UserTableFromConfiguration);
        Assert.True(options.Origins.UserSchemaFromConfiguration);
        Assert.True(options.Origins.UserIdColumnFromConfiguration);
        Assert.True(options.Origins.IncludeColumnsFromConfiguration);
        Assert.True(options.Origins.OutputDirectoryFromConfiguration);
        Assert.True(options.Origins.UserMapPathFromConfiguration);
        Assert.True(options.Origins.UatUserInventoryPathFromConfiguration);
        Assert.True(options.Origins.QaUserInventoryPathFromConfiguration);
        Assert.True(options.Origins.SnapshotPathFromConfiguration);
        Assert.True(options.Origins.UserEntityIdentifierFromConfiguration);
        Assert.True(options.Origins.MatchingStrategyFromConfiguration);
        Assert.True(options.Origins.MatchingAttributeFromConfiguration);
        Assert.True(options.Origins.MatchingRegexFromConfiguration);
        Assert.True(options.Origins.FallbackModeFromConfiguration);
        Assert.True(options.Origins.FallbackTargetsFromConfiguration);
        Assert.True(options.Origins.ConnectionStringFromConfiguration);
        Assert.True(options.Origins.IdempotentEmissionFromConfiguration);
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
