using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
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

public class InspectCommandModuleTests
{
    [Fact]
    public async Task Invoke_LoadsModelAndWritesSummary()
    {
        var modelPath = FixtureFile.GetPath("model.micro-physical.json");
        var ingestion = new FakeIngestionService(ModelFixtures.LoadModel("model.micro-physical.json"));

        var services = new ServiceCollection();
        services.AddSingleton<IModelIngestionService>(ingestion);
        services.AddSingleton<InspectCommandModule>();

        await using var provider = services.BuildServiceProvider();
        var module = provider.GetRequiredService<InspectCommandModule>();
        var command = module.BuildCommand();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var exitCode = await parser.InvokeAsync($"inspect --model {modelPath}");

        Assert.Equal(0, exitCode);
        Assert.Equal(modelPath, ingestion.LastPath);
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
