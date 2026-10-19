using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Tests;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// WP 1.6's Done-when: two refs of a classic-minimal repository, each checked out and built at once against the published
/// tool folder, share nothing: two worktrees, two build folders named by the commits, each package its own commit's, and
/// neither build writing in a worktree, in the repository's tree or in the other's folders. Once from two threads of one
/// process, and again from two dbchange processes, where nothing but the worktrees lock orders one process's sweep against
/// the other's At. And a ref's build reads the ref's own MSBuild files alone, never the enclosing checkout's.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class RefBuildTests(PublishedTool tool) : IDisposable
{
    private const string Project = "classic-minimal/ClassicMinimal.sqlproj";

    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    private string Output => Path.Combine(scratch.Root, ".dbchange", "build");

    [Fact]
    [Trait("Category", "fast")]
    public async Task Two_concurrent_builds_of_two_refs_share_nothing()
    {
        var (before, after) = ClassicMinimal();

        var built = await Task.WhenAll(((string[])[before, after]).Select(commit => Task.Run(() =>
        {
            var at = GitTests.Ok(Git.At(scratch.Root, commit));
            return (At: at, Dacpac: GitTests.Ok(Ssdt.Build(at, Project, tool.Folder, Output)).Path);
        })));

        SharedNothing(before, after, built);
    }

    /// <summary>
    /// The Done-when across processes. Two dbchange processes at once make both worktrees; then, twenty-four times, two more
    /// take them again from holders that have exited, released from a thirty-second to three quarters of the first round's
    /// At apart, each ref in turn the later, so that one process's sweep meets the other's worktree while the other is taking
    /// it; then two build both refs at once. No process ends before every At of its round has returned, so every sweep meets
    /// both holders running: after every round both worktrees stand at their commits, and the builds share nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Two_dbchange_processes_building_two_refs_at_once_share_nothing()
    {
        var (before, after) = ClassicMinimal();
        var took = Round([before, after], 0).Max(r => r.Took);
        for (var round = 1; round <= 24; round++)
        {
            Round(round % 2 == 0 ? [before, after] : [after, before], took * round / 32);
        }

        SharedNothing(before, after, Round([before, after], 0, Project, tool.Folder, Output).Select(r => (r.At, r.Dacpac)).ToArray());
    }

    /// <summary>
    /// MSBuild searches upward from a project for Directory.Build.props, Directory.Build.targets, Directory.Packages.props and
    /// Directory.Build.rsp, and from .dbchange/worktrees/&lt;commit&gt;/ that search reaches the enclosing checkout's root (fact 4 of the
    /// specification): a committed tree with no such files, and a working tree whose Directory.Build.props imports a file that does not
    /// exist, built at the commit, builds the commit's project alone. Without the stop files .dbchange/worktrees/ carries, the build fails
    /// with MSB4019.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_ref_s_build_reads_its_own_Directory_Build_props_alone_and_never_the_enclosing_checkout_s()
    {
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var file in (string[])[Project, "classic-minimal/ClassicMinimal.refactorlog", "classic-minimal/Script.PostDeployment.sql",
            "classic-minimal/dbo/Tables/Customer.sql"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch.Root, file))!);
            File.Copy(Path.Combine(golden, file), Path.Combine(scratch.Root, file));
        }

        var commit = scratch.Commit("classic-minimal without MSBuild files", (".gitignore", ".dbchange/\n"));
        scratch.Write(("Directory.Build.props", "<Project><Import Project=\"does-not-exist.targets\" /></Project>\n"));

        var dacpac = GitTests.Ok(Ssdt.Build(GitTests.Ok(Git.At(scratch.Root, commit)), Project, tool.Folder, Output));

        Assert.Equal(["[dbo].[Customer].[GivenName]", "[dbo].[Customer].[Id]"], Columns(dacpac.Path));
    }

    /// <summary>One round of dbchange processes, one per commit in the order released, a gap apart; each commit's worktree stands at it
    /// when all have exited.</summary>
    private List<(Git.Worktree At, long Took, string Dacpac)> Round(string[] commits, long gap, params string[] build)
    {
        var taken = DbChangeProcess.AtOnce(scratch.Root, commits, gap, build);
        foreach (var (at, _, _) in taken)
        {
            Assert.True(File.Exists(Path.Combine(at.Path, Project)),
                "released " + gap + " ms apart, " + at.Path + " was swept from under the dbchange process that took it");
            Assert.Equal(at.Commit, scratch.GitAt(at.Path, "rev-parse", "HEAD"));
        }

        return taken;
    }

    /// <summary>The classic-minimal project committed, then committed again with Customer given an Email column; the two commits.</summary>
    private (string Before, string After) ClassicMinimal()
    {
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var file in (string[])["Directory.Build.props", "Directory.Packages.props", Project,
            "classic-minimal/ClassicMinimal.refactorlog", "classic-minimal/Script.PostDeployment.sql",
            "classic-minimal/dbo/Tables/Customer.sql"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch.Root, file))!);
            File.Copy(Path.Combine(golden, file), Path.Combine(scratch.Root, file));
        }

        var before = scratch.Commit("classic-minimal", (".gitignore", ".dbchange/\n"));
        return (before, scratch.Commit("Customer gains Email", ("classic-minimal/dbo/Tables/Customer.sql",
            "CREATE TABLE [dbo].[Customer]\n(\n    [Id] INT NOT NULL CONSTRAINT [PK_Customer] PRIMARY KEY,\n    [GivenName] NVARCHAR(100) NULL,\n    [Email] NVARCHAR(320) NULL\n);\n")));
    }

    /// <summary>
    /// The two refs' worktrees and build folders, named by their commits; each package with its own commit's columns; no
    /// worktree or build folder naming the other commit; nothing written in a worktree or in the repository's tree.
    /// </summary>
    private void SharedNothing(string before, string after, (Git.Worktree At, string Dacpac)[] built)
    {
        Assert.Equal([before, after], built.Select(b => b.At.Commit));
        Assert.Equal(
            [Path.Combine(scratch.Root, ".dbchange", "worktrees", before), Path.Combine(scratch.Root, ".dbchange", "worktrees", after)],
            built.Select(b => b.At.Path));
        Assert.Equal([Path.Combine(Output, before), Path.Combine(Output, after)],
            built.Select(b => Path.GetDirectoryName(Path.GetDirectoryName(b.Dacpac))));
        Assert.Equal(["[dbo].[Customer].[GivenName]", "[dbo].[Customer].[Id]"], Columns(built[0].Dacpac));
        Assert.Equal(["[dbo].[Customer].[Email]", "[dbo].[Customer].[GivenName]", "[dbo].[Customer].[Id]"], Columns(built[1].Dacpac));
        foreach (var (mine, theirs) in ((int[])[0, 1]).Select(i => (built[i], built[1 - i])))
        {
            Assert.Equal("", scratch.GitAt(mine.At.Path, "status", "--porcelain", "--ignored", "--untracked-files=all"));
            foreach (var folder in (string[])[mine.At.Path, Path.GetDirectoryName(mine.Dacpac)!])
            {
                Assert.Contains(Bytes(folder), text => text.Contains(mine.At.Commit, StringComparison.Ordinal));
                Assert.DoesNotContain(Bytes(folder), text => text.Contains(theirs.At.Commit, StringComparison.Ordinal));
            }
        }

        Assert.Equal("", scratch.Git("status", "--porcelain", "--untracked-files=all"));
    }

    /// <summary>The columns a package holds, by name, in ordinal order.</summary>
    private static List<string> Columns(string dacpac)
    {
        using var package = GitTests.Ok(Ssdt.Open(dacpac));
        return package.Model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass).SelectMany(t => t.GetReferenced(Table.Columns))
            .Select(c => c.Name.ToString()).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every file under a folder, its bytes read as Latin-1 so any path it records can be searched for.</summary>
    private static IEnumerable<string> Bytes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Encoding.Latin1.GetString(File.ReadAllBytes(f)));
}
