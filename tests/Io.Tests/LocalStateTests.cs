using System;
using System.IO;
using System.Linq;
using Estate.Kernel;
using Xunit;
using static Estate.Tests.Expect;

namespace Estate.Io.Tests;

/// <summary>
/// io/LocalState (R7): the .estate/ folder ignores itself in a repository whose .gitignore does not name it, the worktrees' folder
/// carries the stop files a ref's build needs and nothing else does, a file that exists is never written over, and a folder that
/// cannot be made is file.unwritable.
/// </summary>
public sealed class LocalStateTests : IDisposable
{
    private static readonly string[] Stops = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Directory.Build.rsp"];

    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "R3")]
    public void Git_lists_nothing_estate_keeps_in_a_repository_whose_gitignore_does_not_name_dot_estate()
    {
        var commit = scratch.Commit("first", ("a.sql", "SELECT 1;\n"));   // no .gitignore
        var state = new LocalState(scratch.Root);

        Value(Git.At(scratch.Root, commit));
        Value(Write.Text(state.Copies, "{ \"copies\": [] }\n"));
        Value(Write.Append(Path.Combine(state.Runs, "20260925T000000Z-1-ab", "queries.log"), "-- one\nSELECT 1;\nGO\n"));

        Assert.Equal("", scratch.Git("status", "--porcelain", "--ignored=no", "--untracked-files=all"));
        Assert.Equal(".estate/.gitignore:1:*\t.estate/copies.json", scratch.Git("check-ignore", "-v", ".estate/copies.json"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Made_writes_the_stop_files_for_the_worktrees_alone_and_never_over_a_file_that_exists()
    {
        var state = new LocalState(scratch.Root);

        Value(state.Made(state.Runs));
        var elsewhere = Stops.Where(stop => File.Exists(Path.Combine(state.Runs, stop)) || File.Exists(Path.Combine(state.Folder, stop))).ToList();
        Value(state.Made(state.Worktree("0123")));
        var written = Stops.Select(stop => File.ReadAllText(Path.Combine(state.Worktrees, stop))).ToList();
        File.WriteAllText(Path.Combine(state.Worktrees, "Directory.Build.rsp"), "-v:n\n");
        File.WriteAllText(Path.Combine(state.Folder, ".gitignore"), "*\n!keep\n");
        Value(state.Made(state.Worktrees));

        Assert.Empty(elsewhere);
        Assert.Equal(["<Project />\n", "<Project />\n", "<Project />\n", ""], written);
        Assert.Equal("-v:n\n", File.ReadAllText(Path.Combine(state.Worktrees, "Directory.Build.rsp")));
        Assert.Equal("*\n!keep\n", File.ReadAllText(Path.Combine(state.Folder, ".gitignore")));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Made_where_a_file_stands_in_the_folder_s_way_is_file_unwritable_naming_the_folder()
    {
        File.WriteAllText(Path.Combine(scratch.Root, ".estate"), "");
        var state = new LocalState(scratch.Root);

        var error = Failed(state.Made(state.Worktrees), "file.unwritable");

        Assert.StartsWith(state.Worktrees + " cannot be written: ", error.Message, StringComparison.Ordinal);
    }
}
