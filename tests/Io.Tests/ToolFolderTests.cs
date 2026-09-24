using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Budgets.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>dist/estate/, published once per run by ci/publish.ps1 on Windows and ci/publish.sh elsewhere.</summary>
public sealed class PublishedTool
{
    public PublishedTool()
    {
        (int Exit, string Output) published = OperatingSystem.IsWindows()
            ? Command.Run("pwsh", ["-NoProfile", "-File", Path.Combine(Repository.Root, "ci", "publish.ps1")])
            : Command.Run("bash", [Path.Combine(Repository.Root, "ci", "publish.sh")]);
        Assert.True(published.Exit == 0, "ci/publish failed:\n" + published.Output);
        Output = published.Output;
    }

    public string Folder { get; } = Path.Combine(Repository.Root, "dist", "estate");

    public string Output { get; }

    /// <summary>
    /// estate from the folder as any shell with dotnet runs it: <c>dotnet dist/estate/estate.dll</c>. The launcher beside it
    /// finds .NET only where it is installed machine-wide or DOTNET_ROOT names it, and the test host sets DOTNET_ROOT for
    /// its children, so a test of the launcher would pass on a machine whose own shell cannot run it.
    /// </summary>
    public (int Exit, string Output) Run(params string[] arguments) => Command.Run("dotnet", [Path.Combine(Folder, "estate.dll"), .. arguments]);
}

/// <summary>
/// §1 fact 1 on this machine: the published tool folder runs, and a classic project builds against it with no Visual Studio,
/// its dacpac carrying the refactorlog and the post-deploy script.
/// </summary>
public sealed class ToolFolderTests(PublishedTool tool) : IClassFixture<PublishedTool>
{
    [Fact]
    [Trait("Category", "fast")]
    public void The_published_estate_answers_its_version_and_the_publish_prints_the_folder_size_and_how_to_run_it()
    {
        var (exit, output) = tool.Run("--version");

        Assert.True(exit == 0, output);
        Assert.StartsWith("estate 3.0.0", output, StringComparison.Ordinal);
        Assert.Matches(@"dist/estate: \d+ files, \d+ MB", tool.Output);
        Assert.Contains("dotnet dist/estate/estate.dll", tool.Output, StringComparison.Ordinal);
        Assert.Contains("DOTNET_ROOT", tool.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_classic_minimal_project_builds_against_the_tool_folder_with_its_refactorlog_and_post_deploy_script()
    {
        // A copy under .estate/ (ignored) with tests/Golden/'s stop files, so each run builds fresh and the engine's props stay out.
        var golden = Path.Combine(Repository.Root, ".estate", "golden", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Copy(Path.Combine(Repository.Root, "tests", "Golden"), golden);
            var project = Path.Combine(golden, "classic-minimal", "ClassicMinimal.sqlproj");

            var (exit, log) = Command.Run("dotnet",
            [
                "build", project, "-c", "Release", "-nologo", "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true",
                "-p:NETCoreTargetsPath=" + tool.Folder, "-p:SQLDBExtensionsRefPath=" + tool.Folder, "-p:TargetFrameworkRootPath=" + Path.Combine(tool.Folder, "refasm"),
            ]);

            Assert.True(exit == 0, log);
            using var dacpac = ZipFile.OpenRead(Path.Combine(golden, "classic-minimal", "bin", "Release", "ClassicMinimal.dacpac"));
            Assert.Superset(new HashSet<string>(["model.xml", "refactor.xml", "postdeploy.sql"]), dacpac.Entries.Select(e => e.FullName).ToHashSet());
            Assert.Contains("[dbo].[Customer].[FirstName]", Text(dacpac, "refactor.xml"), StringComparison.Ordinal);
            Assert.Contains("post-deploy ran", Text(dacpac, "postdeploy.sql"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(golden, recursive: true);
        }
    }

    /// <summary>M0 exit 3 from the published folder: DEGRADED, the tool folder found beside it, a remedy on every block, M1 not claimed.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_published_doctor_finds_its_tool_folder_and_does_not_claim_M1()
    {
        var (exit, output) = tool.Run("doctor", "--json");

        var answer = JsonNode.Parse(output)!;
        var line = (string)answer["verdict"]!["message"]!;
        var findings = answer["findings"]!.AsArray().Select(f => f!).ToList();
        Assert.Equal(6, exit);
        Assert.StartsWith("estate doctor DEGRADED | ", line, StringComparison.Ordinal);
        Assert.Contains(" | tool=published | ", line, StringComparison.Ordinal);
        Assert.EndsWith("arrive in M1 (Read)", line, StringComparison.Ordinal);
        Assert.DoesNotContain("READY", output, StringComparison.Ordinal);
        Assert.DoesNotContain(findings, f => (string)f["code"]! == "doctor.tool");
        Assert.All(findings, f => Assert.False(string.IsNullOrEmpty((string?)f["remedy"])));
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(to, relative))!);
            File.Copy(file, Path.Combine(to, relative));
        }
    }

    private static string Text(ZipArchive archive, string entry)
    {
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }
}
