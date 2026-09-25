using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Microsoft.SqlServer.Dac.Model;
using Xunit;
using Contract = Estate.Cli.Contract;

namespace Estate.Io.Tests;

/// <summary>
/// io/Git (WP 1.6), over temporary repositories only: a ref as a detached worktree under .estate/worktrees/&lt;commit&gt;/,
/// reused and swept; the merge base of two refs; the paths two refs differ in; and a commit on a named branch pushed to a
/// local bare origin. No test runs git in the engine's own repository.
/// </summary>
public sealed class GitTests : IDisposable
{
    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "fast")]
    public void At_checks_a_ref_out_detached_in_the_worktree_its_commit_names_and_a_second_At_reuses_it()
    {
        var first = scratch.Commit("first", ("a.sql", "SELECT 1;\n"));
        scratch.Git("tag", "v1");
        scratch.Commit("second", ("a.sql", "SELECT 2;\n"));

        var at = Ok(Git.At(scratch.Root, "v1"));
        File.WriteAllText(Path.Combine(at.Path, "left-behind.txt"), "");
        var again = Ok(Git.At(scratch.Root, first[..12]));

        Assert.Equal(new Git.Worktree(Path.Combine(scratch.Root, ".estate", "worktrees", first), first), at);
        Assert.Equal(at, again);
        Assert.True(File.Exists(Path.Combine(again.Path, "left-behind.txt")), "the second At made the worktree afresh");
        Assert.Equal("SELECT 1;\n", File.ReadAllText(Path.Combine(at.Path, "a.sql")));
        Assert.Equal("HEAD", scratch.GitAt(at.Path, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(2, scratch.Worktrees().Count);
        Assert.Equal("build.no-project", Failed(Ssdt.Build(at, Path.Combine(scratch.Root, "a.sqlproj"), scratch.Root, scratch.Root)).Code);   // a ref's project is named from its root
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_sweep_removes_the_worktree_no_running_estate_holds_keeps_the_held_one_and_prunes_gits_record()
    {
        var held = Ok(Git.At(scratch.Root, scratch.Commit("held", ("a.sql", "SELECT 1;\n"))));
        var left = Ok(Git.At(scratch.Root, scratch.Commit("left", ("a.sql", "SELECT 2;\n"))));
        var folder = Path.GetDirectoryName(left.Path)!;
        var hold = Assert.Single(Directory.GetFiles(folder, left.Commit + ".*.lock"));
        File.Move(hold, Path.Combine(folder, left.Commit + "." + Exited() + ".lock"));   // the estate that took it has since exited

        Assert.Equal([left.Commit], Ok(Git.Sweep(scratch.Root)));

        Assert.True(Directory.Exists(held.Path));
        Assert.False(Directory.Exists(left.Path));
        Assert.Equal([held.Commit + "." + Environment.ProcessId + ".lock"], Directory.GetFiles(folder, "*.lock").Select(Path.GetFileName));
        Assert.Equal(2, scratch.Worktrees().Count);
        Assert.DoesNotContain(scratch.Worktrees(), w => w.EndsWith(left.Commit, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    public async Task At_and_a_sweep_wait_while_another_estate_holds_the_worktrees_turn_and_go_on_when_it_ends()
    {
        var first = Ok(Git.At(scratch.Root, scratch.Commit("first", ("a.sql", "SELECT 1;\n"))));
        var turn = new FileStream(Path.Combine(scratch.Root, ".estate", "worktrees", ".turn"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var (at, sweep) = (Task.Run(() => Git.At(scratch.Root, first.Commit)), Task.Run(() => Git.Sweep(scratch.Root)));
        await Task.Delay(TimeSpan.FromSeconds(1));
        var waited = !at.IsCompleted && !sweep.IsCompleted;
        await turn.DisposeAsync();

        Assert.True(waited, "At or a sweep went on in another estate's turn");
        Assert.Equal(first, Ok(await at));
        Assert.Empty(Ok(await sweep));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void MergeBase_finds_where_a_branch_left_main_and_ChangedPaths_lists_the_paths_two_refs_differ_in_from_the_root_in_ordinal_order()
    {
        var fork = scratch.Commit("fork", ("README.md", "estate\n"), ("dbo/Tables/Customer.sql", "c\n"), ("dbo/Tables/Order.sql", "o\n"));
        scratch.Git("switch", "-q", "-c", "feature");
        scratch.Git("mv", "dbo/Tables/Order.sql", "dbo/Tables/Orders.sql");
        var feature = scratch.Commit("feature", ("dbo/Tables/Customer.sql", "c2\n"), ("dbo/Tables/Café.sql", "é\n"), ("a.md", "a\n"), ("Z.md", "z\n"));
        scratch.Git("switch", "-q", "main");
        scratch.Commit("main moves on", ("README.md", "estate, later\n"));

        Assert.Equal(fork, Ok(Git.MergeBase(scratch.Root, "main", "feature")));
        Assert.Equal(
            ["Z.md", "a.md", "dbo/Tables/Café.sql", "dbo/Tables/Customer.sql", "dbo/Tables/Order.sql", "dbo/Tables/Orders.sql"],
            Ok(Git.ChangedPaths(Path.Combine(scratch.Root, "dbo"), fork, "feature")));
        Assert.Equal(
            ["README.md", "Z.md", "a.md", "dbo/Tables/Café.sql", "dbo/Tables/Customer.sql", "dbo/Tables/Order.sql", "dbo/Tables/Orders.sql"],
            Ok(Git.ChangedPaths(scratch.Root, "main", feature)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void CommitAndPush_publishes_exactly_the_named_branch_with_exactly_the_given_paths_and_leaves_the_callers_checkout_as_it_was()
    {
        var origin = scratch.Origin();
        var head = scratch.Commit("the estate", ("estate/evidence.shape.json", "{}\n"), ("README.md", "estate\n"));
        scratch.Git("push", "-q", "origin", "main");
        scratch.Write(("estate/evidence.shape.json", "{ \"sites\": [] }\n"), ("estate/ledgers/row-tiers.md", "| table | tier |\n"), ("README.md", "estate, edited\n"));
        scratch.Git("add", "README.md");   // the caller's own staged change stays theirs

        var pushed = Ok(Git.CommitAndPush(scratch.Root, ["estate/evidence.shape.json", "estate/ledgers/row-tiers.md"], "profile: dev's evidence", "estate/evidence-dev"));

        Assert.Equal("estate/evidence-dev", pushed.Branch);
        Assert.Equal(["refs/heads/estate/evidence-dev " + pushed.Commit, "refs/heads/main " + head], scratch.GitAt(origin, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads").Split('\n'));
        Assert.Equal(["estate/evidence.shape.json", "estate/ledgers/row-tiers.md"], scratch.GitAt(origin, "diff-tree", "-r", "--name-only", "--no-commit-id", head, pushed.Commit).Split('\n'));
        Assert.Equal(head + "\nEstate Test\nprofile: dev's evidence", scratch.GitAt(origin, "log", "-1", "--format=%P%n%an%n%B", pushed.Commit));
        Assert.Equal("{ \"sites\": [] }", scratch.GitAt(origin, "show", pushed.Commit + ":estate/evidence.shape.json"));
        Assert.Equal(("refs/heads/main", head), (scratch.Git("symbolic-ref", "HEAD"), scratch.Git("rev-parse", "HEAD")));
        Assert.Equal(["M  README.md", " M estate/evidence.shape.json", "?? estate/ledgers/"], scratch.Git("status", "--porcelain").Split('\n'));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_taken_or_malformed_branch_is_exit_9_and_an_origin_that_does_not_answer_is_exit_4_with_no_credential_printed_and_no_branch_left()
    {
        scratch.Origin();
        scratch.Commit("the estate", ("estate/evidence.shape.json", "{}\n"));
        scratch.Write(("estate/evidence.shape.json", "{ }\n"));
        string[] paths = ["estate/evidence.shape.json"];
        Ok(Git.CommitAndPush(scratch.Root, paths, "first", "estate/evidence"));

        var taken = Failed(Git.CommitAndPush(scratch.Root, paths, "again", "estate/evidence"));
        scratch.Git("branch", "-q", "-D", "estate/evidence");
        var takenAtOrigin = Failed(Git.CommitAndPush(scratch.Root, paths, "again", "estate/evidence"));
        var malformed = Failed(Git.CommitAndPush(scratch.Root, paths, "again", "estate/..evidence"));
        scratch.Git("remote", "set-url", "origin", "https://estate:s3cret-token@127.0.0.1:9/estate.git");
        var silent = Failed(Git.CommitAndPush(scratch.Root, paths, "again", "estate/elsewhere"));

        Assert.Equal(("branch.taken", 9), (taken.Code, Contract.Exit(taken)));
        Assert.Equal(("branch.taken", 9), (takenAtOrigin.Code, Contract.Exit(takenAtOrigin)));
        Assert.Contains("the origin", takenAtOrigin.Message, StringComparison.Ordinal);
        Assert.Equal(("branch.malformed", 9), (malformed.Code, Contract.Exit(malformed)));
        Assert.Equal(("origin.unreachable", 4), (silent.Code, Contract.Exit(silent)));
        Assert.DoesNotContain("s3cret-token", silent.Message + silent.Remedy, StringComparison.Ordinal);
        Assert.Equal("", scratch.Git("branch", "--list", "estate/elsewhere", "estate/..evidence"));
        Assert.Equal("", scratch.Git("diff", "--cached", "--name-only"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_ref_that_names_no_commit_and_two_unrelated_refs_are_exit_1_and_a_folder_that_is_no_repository_is_exit_6()
    {
        scratch.Commit("main", ("a.sql", "SELECT 1;\n"));
        scratch.Git("switch", "-q", "--orphan", "unrelated");
        scratch.Commit("unrelated", ("b.sql", "SELECT 2;\n"));

        var unresolved = Failed(Git.At(scratch.Root, "no-such-tag"));
        var unrelated = Failed(Git.MergeBase(scratch.Root, "main", "unrelated"));
        var nowhere = Failed(Git.ChangedPaths(Path.Combine(scratch.Root, "no-such-folder"), "main", "unrelated"));

        Assert.Equal(("ref.unresolved", 1), (unresolved.Code, Contract.Exit(unresolved)));
        Assert.Contains("'no-such-tag'", unresolved.Message, StringComparison.Ordinal);
        Assert.Equal(("ref.unrelated", 1), (unrelated.Code, Contract.Exit(unrelated)));
        Assert.Equal(("git.not-a-repository", 6), (nowhere.Code, Contract.Exit(nowhere)));
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, ".estate", "worktrees")) && Directory.EnumerateDirectories(Path.Combine(scratch.Root, ".estate", "worktrees")).Any());
    }

    /// <summary>
    /// io/Git reads git's English "not a git repository (or any ...)" to tell a folder in no repository from a failed search, so git
    /// runs with LC_ALL=C, and without LANGUAGE and LC_MESSAGES, which GNU gettext would otherwise read to choose a translation. A
    /// stand-in for git prints the environment it is given to its error stream and exits 2, so HoldingOf fails with git.failed and
    /// quotes that environment: LC_ALL=C is in it, and LANGUAGE and LC_MESSAGES, set in this process, are not.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Git_runs_with_LC_ALL_C_and_without_the_callers_LANGUAGE_and_LC_MESSAGES()
    {
        var git = Path.Combine(scratch.Root, OperatingSystem.IsWindows() ? "git-environment.cmd" : "git-environment.sh");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(git, "@set 1>&2\r\n@exit /b 2\r\n");
        }
        else
        {
            File.WriteAllText(git, "#!/bin/sh\nenv >&2\nexit 2\n");
            File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var asked = (Language: Environment.GetEnvironmentVariable("LANGUAGE"), Messages: Environment.GetEnvironmentVariable("LC_MESSAGES"));
        Environment.SetEnvironmentVariable("LANGUAGE", "de");
        Environment.SetEnvironmentVariable("LC_MESSAGES", "de_DE.UTF-8");
        Error error;
        try
        {
            error = Failed(Git.HoldingOf(scratch.Root, Path.Combine(scratch.Root, "dev.connection"), git));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LANGUAGE", asked.Language);
            Environment.SetEnvironmentVariable("LC_MESSAGES", asked.Messages);
        }

        Assert.Equal("git.failed", error.Code);
        Assert.StartsWith("git rev-parse failed: ", error.Message, StringComparison.Ordinal);
        var variables = error.Message["git rev-parse failed: ".Length..].Split('\n').Select(line => line.Trim()).ToList();
        Assert.Contains("LC_ALL=C", variables);
        Assert.DoesNotContain(variables, line => line.StartsWith("LANGUAGE=", StringComparison.OrdinalIgnoreCase) || line.StartsWith("LC_MESSAGES=", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The id of a process that has exited, as an estate's has once it ends.</summary>
    private static int Exited()
    {
        using var process = Process.Start(new ProcessStartInfo("git", ["--version"]) { RedirectStandardOutput = true })!;
        var id = process.Id;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return id;
    }

    internal static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    internal static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}

/// <summary>
/// WP 1.6's Done-when: two refs of a classic-minimal repository, each checked out and built at once against the published
/// tool folder, share nothing: two worktrees, two build folders named by the commits, each package its own commit's, and
/// neither build writing in a worktree, in the repository's tree or in the other's folders. Once from two threads of one
/// process, and again from two estate processes, where nothing but the worktrees' turn orders one process's sweep against
/// the other's At.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class RefBuildTests(PublishedTool tool) : IDisposable
{
    private const string Project = "classic-minimal/ClassicMinimal.sqlproj";

    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    private string Output => Path.Combine(scratch.Root, ".estate", "build");

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
    /// The Done-when across processes. Two estate processes at once make both worktrees; then, twenty-four times, two more
    /// take them again from holders that have exited, released from a thirty-second to three quarters of the first round's
    /// At apart, each ref in turn the later, so that one process's sweep meets the other's worktree while the other is taking
    /// it; then two build both refs at once. No process ends before every At of its round has returned, so every sweep meets
    /// both holders running: after every round both worktrees stand at their commits, and the builds share nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Two_estate_processes_building_two_refs_at_once_share_nothing()
    {
        var (before, after) = ClassicMinimal();
        var took = Round([before, after], 0).Max(r => r.Took);
        for (var round = 1; round <= 24; round++)
        {
            Round(round % 2 == 0 ? [before, after] : [after, before], took * round / 32);
        }

        SharedNothing(before, after, Round([before, after], 0, Project, tool.Folder, Output).Select(r => (r.At, r.Dacpac)).ToArray());
    }

    /// <summary>One round of estate processes, one per commit in the order released, a gap apart; each commit's worktree stands at it when all have exited.</summary>
    private List<(Git.Worktree At, long Took, string Dacpac)> Round(string[] commits, long gap, params string[] build)
    {
        var taken = EstateProcess.AtOnce(scratch.Root, commits, gap, build);
        foreach (var (at, _, _) in taken)
        {
            Assert.True(File.Exists(Path.Combine(at.Path, Project)), "released " + gap + " ms apart, " + at.Path + " was swept from under the estate process that took it");
            Assert.Equal(at.Commit, scratch.GitAt(at.Path, "rev-parse", "HEAD"));
        }

        return taken;
    }

    /// <summary>The classic-minimal project committed, then committed again with Customer given an Email column; the two commits.</summary>
    private (string Before, string After) ClassicMinimal()
    {
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var file in (string[])["Directory.Build.props", "Directory.Packages.props", Project, "classic-minimal/ClassicMinimal.refactorlog", "classic-minimal/Script.PostDeployment.sql", "classic-minimal/dbo/Tables/Customer.sql"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch.Root, file))!);
            File.Copy(Path.Combine(golden, file), Path.Combine(scratch.Root, file));
        }

        var before = scratch.Commit("classic-minimal", (".gitignore", ".estate/\n"));
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
        Assert.Equal([Path.Combine(scratch.Root, ".estate", "worktrees", before), Path.Combine(scratch.Root, ".estate", "worktrees", after)], built.Select(b => b.At.Path));
        Assert.Equal([Path.Combine(Output, before), Path.Combine(Output, after)], built.Select(b => Path.GetDirectoryName(b.Dacpac)));
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
        using var package = GitTests.Ok(Ssdt.Load(dacpac));
        return package.Model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass).SelectMany(t => t.GetReferenced(Table.Columns)).Select(c => c.Name.ToString()).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every file under a folder, its bytes read as Latin-1 so any path it records can be searched for.</summary>
    private static IEnumerable<string> Bytes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Encoding.Latin1.GetString(File.ReadAllBytes(f)));
}

/// <summary>
/// A git repository in a folder of its own under the temporary folder, outside every checkout: its user, email, signing
/// and line endings set in it alone, git told never to look above it, and its root checked before any test runs git in it.
/// </summary>
internal sealed class Scratch : IDisposable
{
    private readonly string parent = Directory.CreateTempSubdirectory("estate-git-").FullName;

    public Scratch()
    {
        var root = Path.Combine(parent, "repository");
        Run(parent, "init", "-q", "--initial-branch=main", root);
        Root = Path.GetFullPath(Run(root, "rev-parse", "--show-toplevel"));
        Assert.Equal(root, Root, ignoreCase: true);   // git found this repository, and no other
        foreach (var (key, value) in ((string, string)[])[("user.name", "Estate Test"), ("user.email", "estate-test@example.invalid"), ("commit.gpgsign", "false"), ("core.autocrlf", "false")])
        {
            Git("config", key, value);
        }
    }

    public string Root { get; }

    public string Git(params string[] arguments) => Run(Root, arguments);

    /// <summary>git in a folder of this scratch: a worktree of the repository, or the origin.</summary>
    public string GitAt(string directory, params string[] arguments)
    {
        Assert.StartsWith(parent, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
        return Run(directory, arguments);
    }

    /// <summary>A bare repository beside this one, added as its origin.</summary>
    public string Origin()
    {
        var origin = Path.Combine(parent, "origin.git");
        Run(parent, "init", "-q", "--bare", origin);
        Git("remote", "add", "origin", origin);
        return origin;
    }

    public void Write(params (string Path, string Text)[] files)
    {
        foreach (var (path, text) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, path))!);
            File.WriteAllText(Path.Combine(Root, path), text);
        }
    }

    /// <summary>Writes the files, commits everything the working tree holds, and returns the commit.</summary>
    public string Commit(string message, params (string Path, string Text)[] files)
    {
        Write(files);
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
        return Git("rev-parse", "HEAD");
    }

    /// <summary>The worktrees git records for the repository, as git list's porcelain names them.</summary>
    public List<string> Worktrees() => Git("worktree", "list", "--porcelain").Split('\n').Where(l => l.StartsWith("worktree ", StringComparison.Ordinal)).ToList();

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(parent, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);   // git writes its objects read-only
        }

        Directory.Delete(parent, recursive: true);
    }

    private string Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory, StandardOutputEncoding = new UTF8Encoding(false),
        };
        start.Environment["GIT_CEILING_DIRECTORIES"] = parent;
        foreach (var variable in (string[])["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR"])
        {
            start.Environment.Remove(variable);
        }

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "git " + string.Join(' ', arguments) + " exited " + process.ExitCode + ": " + errors.Result);
        return output.TrimEnd();
    }
}
