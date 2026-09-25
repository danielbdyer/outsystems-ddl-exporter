using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Estate.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// Fact 2 of the specification, end to end: the published estate, run in a folder that is no repository and holds a planted git, with
/// NoDefaultCurrentDirectoryInExePath removed from its environment as a default Windows session has it, answers git.not-a-repository
/// quoting git's own "not a git repository". Before io/Git ran git through io/Command, on Windows, the planted program answered for git:
/// the same code, with an empty quotation.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class PlantedProgramTests(PublishedTool tool) : IDisposable
{
    private readonly ScratchFolder scratch = ScratchFolder.Temporary("cwd");

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "build")]
    public void The_published_estate_runs_the_PATH_s_git_and_never_a_git_planted_in_its_working_directory()
    {
        CommandTests.Plant(scratch.Path, "git");

        var ran = new Command("dotnet", [Path.Combine(tool.Folder, "estate.dll"), "read", "--from", "ref:HEAD", "--json"], TimeSpan.FromMinutes(2))
        {
            Directory = scratch.Path, Environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["NoDefaultCurrentDirectoryInExePath"] = null },
        }.Run();

        var exited = Assert.IsType<Ran.Exited>(ran);
        var answer = JsonNode.Parse(exited.Output)!;
        Assert.True((exited.Code, (string?)answer["findings"]![0]!["code"]) == (6, "git.not-a-repository"), exited.Output + exited.Errors);
        Assert.True(((string?)answer["findings"]![0]!["message"])!.Contains("not a git repository", StringComparison.Ordinal), exited.Output);   // git's own words, which the planted program never prints
        Assert.DoesNotContain("planted", exited.Output, StringComparison.OrdinalIgnoreCase);
    }
}
