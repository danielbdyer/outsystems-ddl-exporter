using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Pipeline.Hosting;
using Osm.Pipeline.Hosting.Verbs;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class InspectCommandFactoryTests
{
    [Fact]
    public void Invoke_PassesModelPathToVerb()
    {
        var fakeVerb = new FakeVerb();
        var services = new ServiceCollection();
        services.AddSingleton<IPipelineVerb<InspectModelVerbOptions>>(fakeVerb);
        services.AddSingleton<InspectCommandFactory>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<InspectCommandFactory>();
        var root = new RootCommand { factory.Create() };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();

        parser.Invoke(new[] { "inspect", "--model", "model.json" });

        Assert.Equal("model.json", fakeVerb.LastOptions?.ModelPath);
    }

    private sealed class FakeVerb : IPipelineVerb<InspectModelVerbOptions>
    {
        public string Name => "inspect";

        public Type OptionsType => typeof(InspectModelVerbOptions);

        public InspectModelVerbOptions? LastOptions { get; private set; }

        public PipelineVerbResult ResultToReturn { get; set; } = new(0);

        public Task<PipelineVerbResult> RunAsync(object options, CancellationToken cancellationToken = default)
            => RunAsync((InspectModelVerbOptions)options, cancellationToken);

        public Task<PipelineVerbResult> RunAsync(InspectModelVerbOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(ResultToReturn);
        }
    }
}
