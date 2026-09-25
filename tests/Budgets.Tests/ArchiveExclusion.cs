using System;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// archive/ is outside v3: its own Directory.Build.props, Directory.Packages.props (central versions off) and
/// .editorconfig (root = true) keep v3's settings out of archive/; no v3 project reaches into it; and no budget,
/// lint or manifest check counts it, because on the day the archive landed every one of them would have gone red.
/// </summary>
public sealed class ArchiveExclusion
{
    [Fact]
    [Trait("Category", "fast")]
    public void The_archive_stops_the_walk_up_from_every_v3_setting()
    {
        var packages = Repository.Xml("archive/Directory.Packages.props").Descendants().Where(e => e.Name.LocalName == "ManagePackageVersionsCentrally");
        var editor = Repository.Lines("archive/.editorconfig").Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));

        Assert.All(Repository.Files.Where(f => f.StartsWith("Directory.", StringComparison.Ordinal) || f == ".editorconfig"), f => Assert.Contains("archive/" + f, Repository.Files));
        Assert.Equal(["false"], packages.Select(e => e.Value));
        Assert.Equal("root = true", editor.First());
        Assert.DoesNotContain(Repository.Xml("archive/Directory.Build.props").Descendants(), e => e.Name.LocalName == "Import");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void No_v3_project_reaches_into_the_archive()
    {
        var reaching = Repository.MsBuildFiles.SelectMany(f => Repository.PathsNamedBy(f).Where(p => p.StartsWith("archive/", StringComparison.Ordinal)).Select(p => f + " names " + p));
        var inSolution = Repository.Lines("Estate.sln").Where(l => l.StartsWith("Project(", StringComparison.Ordinal) && l.Contains("archive", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(reaching);
        Assert.Empty(inSolution);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void No_budget_or_document_law_counts_the_archive()
    {
        var budgets = JsonNode.Parse(Repository.Read("ci/budgets.json"))!["budgets"]!.AsObject();
        var counted = budgets.SelectMany(b => Budgets.Counted(b.Value!)).Where(f => f.Path.StartsWith("archive/", StringComparison.Ordinal));

        Assert.Empty(counted);
        Assert.DoesNotContain(Documents.HandWritten, p => p.StartsWith("archive/", StringComparison.Ordinal));
        Assert.Contains(Repository.Files, f => f.StartsWith("archive/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal));   // there is something to exclude
    }
}
