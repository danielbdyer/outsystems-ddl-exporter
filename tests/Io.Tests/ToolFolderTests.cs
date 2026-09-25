using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Budgets.Tests;
using Xunit;
using Contract = Estate.Cli.Contract;

namespace Estate.Io.Tests;

/// <summary>
/// dist/estate/, published once per run by ci/publish.ps1 on Windows and ci/publish.sh elsewhere, and shared by every class
/// that builds against it, so no class's publish deletes the folder under another's build.
/// </summary>
public sealed class PublishedTool
{
    private static readonly Lazy<(int Exit, string Output)> Publication = new(() => (OperatingSystem.IsWindows()
        ? Programs.InRepository("pwsh", "-NoProfile", "-File", Path.Combine(Repository.Root, "ci", "publish.ps1"))
        : Programs.InRepository("bash", Path.Combine(Repository.Root, "ci", "publish.sh"))).Finish().Joined());

    public PublishedTool()
    {
        var published = Publication.Value;
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
    public (int Exit, string Output) Run(params string[] arguments) => Programs.InRepository("dotnet", [Path.Combine(Folder, "estate.dll"), .. arguments]).Finish().Joined();

    /// <summary>estate from the folder as its own process, run in <paramref name="estateRoot"/>, so the executable's own runtime settings load the model.</summary>
    public (int Exit, string Output) RunAt(string estateRoot, params string[] arguments) =>
        (Programs.InRepository("dotnet", [Path.Combine(Folder, "estate.dll"), .. arguments]) with { Directory = estateRoot }).Finish().Joined();

    /// <summary>A classic .sqlproj built against the folder (section 1 fact 1): the committed DacFx's targets, no Visual Studio, no node left holding the folder.</summary>
    public (int Exit, string Output) Build(string project) => Programs.InRepository("dotnet",
        "build", project, "-c", "Release", "-nologo", "-nodeReuse:false", "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true",
        "-p:NETCoreTargetsPath=" + Folder, "-p:SQLDBExtensionsRefPath=" + Folder, "-p:TargetFrameworkRootPath=" + Path.Combine(Folder, "refasm")).Finish().Joined();
}

/// <summary>
/// The classes that use dist/estate/ share one publish and run one after another: a second publish would delete the folder
/// under a build that is loading DacFx from it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PublishedToolCollection : ICollectionFixture<PublishedTool>
{
    public const string Name = "the published tool folder";
}

/// <summary>§1 fact 1 on this machine: the published tool folder runs and finds itself; SsdtTests and the golden project (SpikeTests) build classic projects against it.</summary>
[Collection(PublishedToolCollection.Name)]
public sealed class ToolFolderTests(PublishedTool tool)
{
    /// <summary>The published estate is this commit's: its version is the one the test's own build carries, so a stale dist/estate/ from another commit fails here.</summary>
    [Fact]
    [Trait("Category", "build")]
    public void The_published_estate_answers_its_version_and_the_publish_prints_the_folder_size_and_how_to_run_it()
    {
        var (exit, output) = tool.Run("--version");

        Assert.True(exit == 0, output);
        Assert.StartsWith("estate " + Contract.Version + "\n", output, StringComparison.Ordinal);
        Assert.Matches(@"dist/estate: \d+ files, \d+ MB", tool.Output);
        Assert.Contains("dotnet dist/estate/estate.dll", tool.Output, StringComparison.Ordinal);
        Assert.Contains("DOTNET_ROOT", tool.Output, StringComparison.Ordinal);
    }

    /// <summary>WP 1.7 from the published folder: READY and exit 0, or DEGRADED and exit 6 with a remedy on every finding; the tool folder found beside it, its DacFx named, and no milestone claimed.</summary>
    [Fact]
    [Trait("Category", "build")]
    [Trait("Value", "A2")]
    [Trait("Exit", "M0.3")]
    public void The_published_doctor_finds_its_tool_folder_and_names_its_DacFx_release()
    {
        var (exit, output) = tool.Run("doctor", "--json");

        var answer = JsonNode.Parse(output)!;
        var line = (string)answer["message"]!;
        var findings = answer["findings"]!.AsArray().Select(f => f!).ToList();
        Assert.Equal(findings.Count == 0 ? (0, "estate doctor READY | ") : (6, "estate doctor DEGRADED | "), (exit, line[..(line.IndexOf('|', StringComparison.Ordinal) + 2)]));
        Assert.Contains(" | tool=published | dacfx=" + DacFx.Version.Match(v => v.ToString(), e => e.Message) + " (", line, StringComparison.Ordinal);
        Assert.DoesNotContain("M1", line, StringComparison.Ordinal);
        Assert.DoesNotContain(findings, f => (string)f["code"]! == "doctor.tool");
        Assert.All(findings, f => Assert.False(string.IsNullOrEmpty((string?)f["remedy"])));
        Assert.Equal(DacFx.Version.Match(v => v.ToString(), e => e.Message), (string?)answer["dacfx"]);
    }

    /// <summary>A tree copied without its bin/ and obj/, so each build under .estate/ starts fresh.</summary>
    internal static void Copy(string from, string to)
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

    internal static string Text(ZipArchive archive, string entry)
    {
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }
}
