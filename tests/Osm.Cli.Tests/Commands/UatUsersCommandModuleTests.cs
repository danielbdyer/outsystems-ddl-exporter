using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli;
using Osm.Cli.Commands;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class UatUsersCommandModuleTests
{
    [Fact]
    public async Task Invoke_ParsesOptions()
    {
        var executor = new FakeUatUsersCommand();

        var services = new ServiceCollection();
        services.AddSingleton<IUatUsersCommand>(executor);
        services.AddSingleton<UatUsersCommandModule>();

        await using var provider = services.BuildServiceProvider();
        var module = provider.GetRequiredService<UatUsersCommandModule>();
        var command = module.BuildCommand();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var args = "uat-users --model model.json --uat-conn Server=.;Database=UAT; --user-schema dbo --user-table dbo.Users --user-id-column UserId --include-columns Name --include-columns Email --out artifacts --user-map map.csv --user-ddl ddl.sql --snapshot snap.json --user-entity-id Identifier";
        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(5, exitCode);
        var options = executor.LastOptions!;
        Assert.Equal("model.json", options.ModelPath);
        Assert.Equal("Server=.;Database=UAT;", options.UatConnectionString);
        Assert.False(options.FromLiveMetadata);
        Assert.Equal("dbo", options.UserSchema);
        Assert.Equal("Users", options.UserTable);
        Assert.Equal("UserId", options.UserIdColumn);
        Assert.Equal(new[] { "Name", "Email" }, options.IncludeColumns);
        Assert.Equal(Path.GetFullPath("artifacts"), options.OutputDirectory);
        Assert.Equal(Path.GetFullPath("map.csv"), options.UserMapPath);
        Assert.Equal(Path.GetFullPath("ddl.sql"), options.AllowedUsersSqlPath);
        Assert.Equal(Path.GetFullPath("snap.json"), options.SnapshotPath);
        Assert.Equal("Identifier", options.UserEntityIdentifier);
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
}
