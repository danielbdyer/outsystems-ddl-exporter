using System;
using System.IO;
using System.Threading.Tasks;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests;

public class FilesystemPermissionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildSsdt_fails_when_output_directory_is_read_only()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));

        using var output = new TempDirectory();
        var outputPath = output.Path;

        FileAttributes? originalAttributes = null;
        UnixFileMode? originalMode = null;

        try
        {
            SetReadOnly(outputPath, ref originalAttributes, ref originalMode);

            var command =
                $"run --project {cliProject} -- build-ssdt --model {modelPath} --profile {profilePath} --static-data {staticDataPath} --out {outputPath}";
            var result = await DotNetCli.RunAsync(repoRoot, command).ConfigureAwait(false);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("pipeline.buildSsdt.output.permissionDenied", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RestorePermissions(outputPath, originalAttributes, originalMode);
        }
    }

    private static void SetReadOnly(
        string directory,
        ref FileAttributes? attributes,
        ref UnixFileMode? mode)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new DirectoryInfo(directory);
            attributes = info.Attributes;
            info.Attributes |= FileAttributes.ReadOnly;
            return;
        }

        mode = Directory.GetUnixFileMode(directory);
        Directory.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static void RestorePermissions(
        string directory,
        FileAttributes? attributes,
        UnixFileMode? mode)
    {
        if (OperatingSystem.IsWindows())
        {
            if (attributes is { } value)
            {
                var info = new DirectoryInfo(directory);
                info.Attributes = value;
            }

            return;
        }

        if (mode is { } unixMode)
        {
            Directory.SetUnixFileMode(directory, unixMode);
        }
    }
}
