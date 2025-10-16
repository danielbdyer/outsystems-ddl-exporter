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

public class ExtractModelCommandFactoryTests
{
    [Fact]
    public void Invoke_BindsOptionsAndReturnsExitCode()
    {
        var fakeVerb = new FakeVerb();
        var services = new ServiceCollection();
        services.AddSingleton<IPipelineVerb<ExtractModelVerbOptions>>(fakeVerb);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<ExtractModelCommandFactory>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ExtractModelCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();

        fakeVerb.ResultToReturn = new PipelineVerbResult(2);
        var args = new[]
        {
            "extract-model",
            "--config", "config.json",
            "--modules", "ModuleA;ModuleB",
            "--include-system-modules",
            "--only-active-attributes",
            "--out", "exported.json",
            "--mock-advanced-sql", "fixture.json",
            "--connection-string", "Sample"
        };
        parser.Invoke(args);

        Assert.NotNull(fakeVerb.LastOptions);
        var options = fakeVerb.LastOptions!;
        Assert.Equal("config.json", options.ConfigPath);
        Assert.Equal(new[] { "ModuleA", "ModuleB" }, options.Overrides.Modules);
        Assert.True(options.Overrides.IncludeSystemModules);
        Assert.True(options.Overrides.OnlyActiveAttributes);
        Assert.Equal("exported.json", options.Overrides.OutputPath);
        Assert.Equal("fixture.json", options.Overrides.MockAdvancedSqlManifest);
        Assert.Equal("Sample", options.Sql.ConnectionString);
    }

    private sealed class FakeVerb : IPipelineVerb<ExtractModelVerbOptions>
    {
        public string Name => "extract-model";

        public Type OptionsType => typeof(ExtractModelVerbOptions);

        public ExtractModelVerbOptions? LastOptions { get; private set; }

        public PipelineVerbResult ResultToReturn { get; set; } = new(0);

        public Task<PipelineVerbResult> RunAsync(object options, CancellationToken cancellationToken = default)
            => RunAsync((ExtractModelVerbOptions)options, cancellationToken);

        public Task<PipelineVerbResult> RunAsync(ExtractModelVerbOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(ResultToReturn);
        }
    }
}
