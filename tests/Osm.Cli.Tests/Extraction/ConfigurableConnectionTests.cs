using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Tests.Support;

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
        var exit = await RunCliAsync(repoRoot, command);
        Assert.Equal(0, exit);

        Assert.True(File.Exists(outputFile));
        using var stream = File.OpenRead(outputFile);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("modules", out var modulesElement));
        Assert.True(modulesElement.GetArrayLength() >= 1);
    }

    private static async Task<int> RunCliAsync(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"CLI exited with code {process.ExitCode}: {error}");
        }

        return process.ExitCode;
    }
}
