using System.IO;
using System.Threading.Tasks;
using Osm.Pipeline.Configuration;
using Xunit;
using Tests.Support;

namespace Osm.Cli.Tests.Configuration;

public class CliConfigurationServiceTests
{
    [Fact]
    public async Task LoadAsync_DefaultsToPipelineJson()
    {
        using var workspace = new TempDirectory();
        var pipelinePath = Path.Combine(workspace.Path, "pipeline.json");
        var pipelineJson = "{ \"model\": { \"path\": \"model.json\" }, \"profile\": { \"path\": \"profile.json\" } }";
        await File.WriteAllTextAsync(pipelinePath, pipelineJson);

        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(workspace.Path);
            var service = new CliConfigurationService(new CliConfigurationLoader());
            var result = await service.LoadAsync(null);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(Path.GetFullPath(pipelinePath), Path.GetFullPath(result.Value.ConfigPath!));
            Assert.Equal(Path.GetFullPath(Path.Combine(workspace.Path, "model.json")), Path.GetFullPath(result.Value.Configuration.ModelPath!));
            Assert.Equal(Path.GetFullPath(Path.Combine(workspace.Path, "profile.json")), Path.GetFullPath(result.Value.Configuration.ProfilePath!));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }
}
