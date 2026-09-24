using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Pipeline.ModelIngestion;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class InspectCommandFactoryTests
{
    [Fact]
    public async Task Invoke_LoadsModelAndWritesSummary()
    {
        var modelPath = FixtureFile.GetPath("model.micro-physical.json");
        var model = ModelFixtures.LoadModel("model.micro-physical.json");
        var ingestion = new FakeIngestionService(model);

        var services = new ServiceCollection();
        services.AddSingleton<IModelIngestionService>(ingestion);
        services.AddSingleton<InspectCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<InspectCommandFactory>();
        var command = factory.Create();
        Assert.NotNull(command.Handler);

        var modelOption = Assert.IsType<Option<string>>(command.Options.Single());
        Assert.Contains("--in", modelOption.Aliases);

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync($"inspect --model {modelPath}", console);

        Assert.Equal(0, exitCode);
        Assert.Equal(modelPath, ingestion.LastPath);
        Assert.Contains("[warning] test-warning", console.Error.ToString());

        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains($"Modules: {model.Modules.Length}", output);
        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        Assert.Contains($"Entities: {entityCount}", output);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(static entity => entity.Attributes.Length));
        Assert.Contains($"Attributes: {attributeCount}", output);
    }

    private sealed class FakeIngestionService : IModelIngestionService
    {
        private readonly OsmModel _model;

        public FakeIngestionService(OsmModel model)
        {
            _model = model;
        }

        public string? LastPath { get; private set; }

        public Task<Result<OsmModel>> LoadFromFileAsync(string path, ICollection<string>? warnings = null, CancellationToken cancellationToken = default, ModelIngestionOptions? options = null)
        {
            LastPath = path;
            warnings?.Add("test-warning");
            return Task.FromResult(Result<OsmModel>.Success(_model));
        }
    }
}
