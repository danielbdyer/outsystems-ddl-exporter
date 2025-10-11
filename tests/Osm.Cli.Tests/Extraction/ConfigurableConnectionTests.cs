using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Tests.Support;
using Xunit;
using CommandResult = Osm.Cli.Tests.DotNetCli.CommandResult;

namespace Osm.Cli.Tests.Extraction;

public class ConfigurableConnectionTests
{
    [Fact]
    public async Task ExtractModel_ShouldWriteJsonUsingFixtureExecutor()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var manifestPath = FixtureFile.GetPath(Path.Combine("extraction", "advanced-sql.manifest.json"));

        using var output = new TempDirectory();
        var outputFile = Path.Combine(output.Path, "edge-case.extracted.json");

        var modules = "AppCore,ExtBilling,Ops";
        var command = $"run --project \"{cliProject}\" -- extract-model --mock-advanced-sql \"{manifestPath}\" --modules \"{modules}\" --out \"{outputFile}\"";
        var result = await RunCliAsync(repoRoot, command);
        Assert.True(result.ExitCode == 0, result.FormatFailureMessage(0));

        Assert.True(File.Exists(outputFile));
        using var stream = File.OpenRead(outputFile);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("modules", out var modulesElement));
        Assert.True(modulesElement.GetArrayLength() >= 1);
    }

    private static async Task<CommandResult> RunCliAsync(string workingDirectory, string arguments)
    {
        var startInfo = DotNetCli.CreateStartInfo(workingDirectory, arguments);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to launch dotnet process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask).ConfigureAwait(false);

        return new CommandResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
