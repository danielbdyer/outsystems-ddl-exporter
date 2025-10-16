using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Pipeline.Hosting;
using Osm.Pipeline.Hosting.Verbs;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class UatUsersCommandFactoryTests
{
    [Fact]
    public void Invoke_CreatesOptionsAndRunsVerb()
    {
        var fakeVerb = new FakeVerb();
        var services = new ServiceCollection();
        services.AddSingleton<IPipelineVerb<UatUsersVerbOptions>>(fakeVerb);
        services.AddSingleton<UatUsersCommandFactory>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<UatUsersCommandFactory>();
        var root = new RootCommand { factory.Create() };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();

        var args = new[]
        {
            "uat-users",
            "--model", "model.json",
            "--uat-conn", "DataSource",
            "--user-ddl", "users.sql",
            "--user-schema", "dbo",
            "--user-table", "User",
            "--user-id-column", "Id"
        };
        parser.Invoke(args);
        Assert.NotNull(fakeVerb.LastOptions);
        var options = fakeVerb.LastOptions!;
        Assert.Equal(Path.GetFullPath("model.json"), options.ModelPath);
        Assert.Equal("DataSource", options.UatConnectionString);
        Assert.Equal("dbo", options.UserSchema);
        Assert.Equal("User", options.UserTable);
        Assert.Equal("Id", options.UserIdColumn);
    }

    private sealed class FakeVerb : IPipelineVerb<UatUsersVerbOptions>
    {
        public string Name => "uat-users";

        public Type OptionsType => typeof(UatUsersVerbOptions);

        public UatUsersVerbOptions? LastOptions { get; private set; }

        public PipelineVerbResult ResultToReturn { get; set; } = new(0);

        public Task<PipelineVerbResult> RunAsync(object options, CancellationToken cancellationToken = default)
            => RunAsync((UatUsersVerbOptions)options, cancellationToken);

        public Task<PipelineVerbResult> RunAsync(UatUsersVerbOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(ResultToReturn);
        }
    }
}
