using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Hosting;
using Osm.Pipeline.Hosting.Verbs;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class DmmCompareCommandFactoryTests
{
    [Fact]
    public void Invoke_BindsOptionsAndPropagatesVerbExitCode()
    {
        var fakeVerb = new FakeVerb();
        var services = new ServiceCollection();
        services.AddSingleton<IPipelineVerb<CompareWithDmmVerbOptions>>(fakeVerb);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<DmmCompareCommandFactory>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DmmCompareCommandFactory>();
        var root = new RootCommand { factory.Create() };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();

        fakeVerb.ResultToReturn = new PipelineVerbResult(5);
        var args = new[]
        {
            "dmm-compare",
            "--config", "config.json",
            "--model", "model.json",
            "--profile", "profile.json",
            "--dmm", "baseline.sql",
            "--out", "diff-out",
            "--modules", "ModuleA",
            "--cache-root", "./cache",
            "--connection-string", "Sample"
        };
        parser.Invoke(args);

        Assert.NotNull(fakeVerb.LastOptions);
        var options = fakeVerb.LastOptions!;
        Assert.Equal("config.json", options.ConfigPath);
        Assert.Equal("model.json", options.Overrides.ModelPath);
        Assert.Equal("profile.json", options.Overrides.ProfilePath);
        Assert.Equal("baseline.sql", options.Overrides.DmmPath);
        Assert.Equal("diff-out", options.Overrides.OutputDirectory);
        Assert.Equal("Sample", options.Sql.ConnectionString);
        Assert.Equal(new[] { "ModuleA" }, options.ModuleFilter.Modules);
    }

    private sealed class FakeVerb : IPipelineVerb<CompareWithDmmVerbOptions>
    {
        public string Name => "dmm-compare";

        public Type OptionsType => typeof(CompareWithDmmVerbOptions);

        public CompareWithDmmVerbOptions? LastOptions { get; private set; }

        public PipelineVerbResult ResultToReturn { get; set; } = new(0);

        public Task<PipelineVerbResult> RunAsync(object options, CancellationToken cancellationToken = default)
            => RunAsync((CompareWithDmmVerbOptions)options, cancellationToken);

        public Task<PipelineVerbResult> RunAsync(CompareWithDmmVerbOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(ResultToReturn);
        }
    }
}
