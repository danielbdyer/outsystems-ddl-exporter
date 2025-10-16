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

public class BuildSsdtCommandFactoryTests
{
    [Fact]
    public void Invoke_BindsAllOptionsAndPropagatesExitCode()
    {
        var fakeVerb = new FakeVerb();
        var services = new ServiceCollection();
        services.AddSingleton<IPipelineVerb<BuildSsdtVerbOptions>>(fakeVerb);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<BuildSsdtCommandFactory>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        fakeVerb.ResultToReturn = new PipelineVerbResult(7);

        var args = new[]
        {
            "build-ssdt",
            "--config", "config.json",
            "--modules", "ModuleA",
            "--include-system-modules",
            "--include-inactive-modules",
            "--allow-missing-primary-key", "Module::*",
            "--cache-root", "./cache",
            "--refresh-cache",
            "--connection-string", "DataSource",
            "--model", "model.json",
            "--profile", "profile.json",
            "--profiler-provider", "fixture",
            "--static-data", "data.json",
            "--out", "output",
            "--rename-table", "Module=Override",
            "--max-degree-of-parallelism", "4",
            "--open-report"
        };
        parser.Invoke(args);

        Assert.NotNull(fakeVerb.LastOptions);
        var options = fakeVerb.LastOptions!;
        Assert.Equal("config.json", options.ConfigPath);
        Assert.Equal("model.json", options.Overrides.ModelPath);
        Assert.Equal("profile.json", options.Overrides.ProfilePath);
        Assert.Equal("output", options.Overrides.OutputDirectory);
        Assert.Equal("fixture", options.Overrides.ProfilerProvider);
        Assert.Equal("data.json", options.Overrides.StaticDataPath);
        Assert.Equal("Module=Override", options.Overrides.RenameOverrides);
        Assert.Equal(4, options.Overrides.MaxDegreeOfParallelism);
        Assert.Equal(new[] { "ModuleA" }, options.ModuleFilter.Modules);
        Assert.True(options.ModuleFilter.IncludeSystemModules);
        Assert.True(options.ModuleFilter.IncludeInactiveModules);
        Assert.Equal(new[] { "Module::*" }, options.ModuleFilter.AllowMissingPrimaryKey);
        Assert.Equal("./cache", options.Cache.Root);
        Assert.True(options.Cache.Refresh);
        Assert.Equal("DataSource", options.Sql.ConnectionString);
        Assert.True(options.OpenReport);
    }

    private sealed class FakeVerb : IPipelineVerb<BuildSsdtVerbOptions>
    {
        public string Name => "build-ssdt";

        public Type OptionsType => typeof(BuildSsdtVerbOptions);

        public BuildSsdtVerbOptions? LastOptions { get; private set; }

        public PipelineVerbResult ResultToReturn { get; set; } = new(0);

        public Task<PipelineVerbResult> RunAsync(object options, CancellationToken cancellationToken = default)
            => RunAsync((BuildSsdtVerbOptions)options, cancellationToken);

        public Task<PipelineVerbResult> RunAsync(BuildSsdtVerbOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(ResultToReturn);
        }
    }
}
