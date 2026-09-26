using System;
using System.IO;
using System.Linq;
using DbChange.Kernel;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// io/LocalState (R7): the .dbchange/ folder ignores itself in a repository whose .gitignore does not name it, the worktrees' folder
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
    public void Git_lists_nothing_dbchange_keeps_in_a_repository_whose_gitignore_does_not_name_dot_dbchange()
    {
        var commit = scratch.Commit("first", ("a.sql", "SELECT 1;\n"));   // no .gitignore
        var state = new LocalState(scratch.Root);

        Value(Git.At(scratch.Root, commit));
        Value(Write.Text(state.Copies, "{ \"copies\": [] }\n"));
        Value(Write.Append(Path.Combine(state.Runs, "20260925T000000Z-1-ab", "queries.log"), "-- one\nSELECT 1;\nGO\n"));

        Assert.Equal("", scratch.Git("status", "--porcelain", "--ignored=no", "--untracked-files=all"));
        Assert.Equal(".dbchange/.gitignore:1:*\t.dbchange/copies.json", scratch.Git("check-ignore", "-v", ".dbchange/copies.json"));
    }

    /// <summary>
    /// M3 of the pre-M2 review: a run's query log, the whole answer written beside it and the copy registry each make their folder
    /// through Made, so a run that reads a database, and one that makes a copy, leave git listing nothing under .dbchange/ though no
    /// worktree was made first. Each once created its folder directly, and a repository whose .gitignore did not name .dbchange/ listed
    /// queries.log. Each writer runs alone in a repository of its own, so no other writer's .gitignore hides its own. The copy is refused
    /// at CREATE DATABASE, by a server on a port nothing listens on, so no SQL Server is needed.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "R3")]
    [InlineData("the query log's first entry")]
    [InlineData("the whole answer beside the log")]
    [InlineData("the copy registry")]
    public void Each_file_a_run_keeps_under_dot_dbchange_leaves_git_listing_nothing_with_no_worktree_made_first(string writer)
    {
        scratch.Commit("first", ("dbchange/environments.json", "{ \"environments\": {} }\n"));   // no .gitignore
        var log = SqlServer.QueryLog.Start(scratch.Root);
        var copy = new SqlServer.Copy(CopyName.Make("host", 1, 0x0a1b2c3d), "Server=localhost,11433", scratch.Root);

        _ = writer switch
        {
            "the query log's first entry" => Value(log.Add(copy, "VIEW DEFINITION", "SELECT 1;", "1 row")),
            "the whole answer beside the log" => Value(log.WriteAnswer("{}\n")),
            _ => Failed(LocalServer.Create(scratch.Root, "Server=tcp:127.0.0.1,1;Connect Timeout=1", log), "server.unreachable").Code,
        };

        var state = new LocalState(scratch.Root);
        Assert.Contains(Directory.EnumerateFiles(state.Folder, "*", SearchOption.AllDirectories), path => Path.GetFileName(path) != ".gitignore");
        Assert.Equal("", scratch.Git("status", "--porcelain", "--ignored=no", "--untracked-files=all"));
    }

    /// <summary>
    /// M2 of the pre-M2 review: a log that cannot be written is file.unwritable naming the run's folder. It once threw, and the verb answered
    /// internal.unexpected, a defect in dbchange with no cause named, for a full disk or a file in the folder's place.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_log_whose_folder_a_file_stands_in_answers_file_unwritable_naming_the_folder()
    {
        var state = new LocalState(scratch.Root);
        Value(state.Made(state.Folder));
        File.WriteAllText(state.Runs, "a file where the runs folder goes\n");
        var log = SqlServer.QueryLog.Start(scratch.Root);
        var copy = new SqlServer.Copy(CopyName.Make("host", 1, 0x0a1b2c3d), "Server=localhost,11433", scratch.Root);

        var error = Failed(log.Add(copy, "VIEW DEFINITION", "SELECT 1;", "1 row"), "file.unwritable");

        Assert.Contains(Path.GetDirectoryName(log.Path)!, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// N4 of the pre-M2 review: a run's folder, made for its first write, leaves the newest fifty run folders, its own among them, and deletes
    /// the older ones with what they hold; .dbchange/runs/ once grew by a folder per command for good.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_new_run_s_folder_leaves_the_newest_fifty_run_folders()
    {
        var state = new LocalState(scratch.Root);
        var older = Enumerable.Range(0, 55).Select(i => Path.Combine(state.Runs, "20260925T0000" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "Z-1-ab")).ToList();
        older.ForEach(run => File.WriteAllText(Path.Combine(Directory.CreateDirectory(run).FullName, "queries.log"), "-- one\n"));
        var newest = Path.Combine(state.Runs, "20260926T000000Z-1-ab");

        Value(state.Made(newest));

        Assert.Equal([.. older.Skip(6), newest], Directory.GetDirectories(state.Runs).Order(StringComparer.Ordinal));
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
        File.WriteAllText(Path.Combine(scratch.Root, ".dbchange"), "");
        var state = new LocalState(scratch.Root);

        var error = Failed(state.Made(state.Worktrees), "file.unwritable");

        Assert.StartsWith(state.Worktrees + " cannot be written: ", error.Message, StringComparison.Ordinal);
    }
}
