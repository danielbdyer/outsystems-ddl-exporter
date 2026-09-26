using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// io/Git (WP 1.6), over temporary repositories only: a ref as a detached worktree under .dbchange/worktrees/&lt;commit&gt;/, reused
/// while unchanged, held by a shared lock and swept; the merge base of two refs; the paths two refs differ in; a commit on a
/// named branch pushed to a local bare origin; and git's answers classified at the boundary, from git's own exit codes and
/// messages, through a stand-in runner where the real git cannot give the answer here. No test runs git in the engine's own repository.
/// </summary>
public sealed class GitTests : IDisposable
{
    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    /// <summary>A worktree is reused while it stands at its commit unchanged: an ignored file in it survives, a changed file makes it afresh.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void At_checks_a_ref_out_detached_in_the_worktree_its_commit_names_and_reuses_it_until_a_file_in_it_changes()
    {
        var first = scratch.Commit("first", (".gitignore", ".dbchange/\n"), ("a.sql", "SELECT 1;\n"));
        scratch.Git("tag", "v1");
        scratch.Commit("second", ("a.sql", "SELECT 2;\n"));

        var at = Ok(Git.At(scratch.Root, "v1"));
        var marker = Path.Combine(at.Path, ".dbchange", "marker");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "");
        var again = Ok(Git.At(scratch.Root, first[..12]));
        var survived = File.Exists(marker);
        File.WriteAllText(Path.Combine(at.Path, "a.sql"), "SELECT 3;\n");
        var afresh = Ok(Git.At(scratch.Root, "v1"));

        Assert.Equal(new Git.Worktree(Path.Combine(scratch.Root, ".dbchange", "worktrees", first), first), at);
        Assert.Equal(at, again);
        Assert.Equal(at, afresh);
        Assert.True(survived, "an ignored file did not survive the reuse of an unchanged worktree");
        Assert.Equal("SELECT 1;\n", File.ReadAllText(Path.Combine(at.Path, "a.sql")));
        Assert.False(File.Exists(marker), "a changed worktree was reused instead of made afresh");
        Assert.Equal("HEAD", scratch.GitAt(at.Path, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(2, scratch.Worktrees().Count);
        Failed(Ssdt.Build(at, Path.Combine(scratch.Root, "a.sqlproj"), scratch.Root, scratch.Root), "build.no-project");   // a ref's project is named from its root
    }

    /// <summary>A worktree left standing at another commit, as a checkout in it leaves it, is removed and made again at the ref's commit.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void At_replaces_a_worktree_that_stands_at_another_commit()
    {
        var first = scratch.Commit("first", ("a.sql", "SELECT 1;\n"));
        var second = scratch.Commit("second", ("a.sql", "SELECT 2;\n"));
        var at = Ok(Git.At(scratch.Root, first));
        scratch.GitAt(at.Path, "checkout", "-q", "--detach", second);

        var again = Ok(Git.At(scratch.Root, first));

        Assert.Equal(at, again);
        Assert.Equal(first, scratch.GitAt(again.Path, "rev-parse", "HEAD"));
        Assert.Equal("SELECT 1;\n", File.ReadAllText(Path.Combine(again.Path, "a.sql")));
    }

    /// <summary>
    /// A stale worktree git cannot remove: on Windows a file under it held open by another program, elsewhere a folder under it whose
    /// contents its owner may not delete. At names the folder to delete by hand instead of building a worktree that stands at the wrong commit.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void At_names_the_folder_to_delete_when_git_cannot_remove_a_stale_worktree()
    {
        var commit = scratch.Commit("first", ("a.sql", "SELECT 1;\n"), ("held/b.sql", "SELECT 2;\n"));
        var at = Ok(Git.At(scratch.Root, commit));
        File.WriteAllText(Path.Combine(at.Path, "a.sql"), "SELECT 3;\n");   // changed, so the worktree is made afresh
        using var open = OperatingSystem.IsWindows() ? new FileStream(Path.Combine(at.Path, "held", "b.sql"), FileMode.Open, FileAccess.Read, FileShare.None) : null;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(at.Path, "held"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            var error = Failed(Git.At(scratch.Root, commit), "git.failed");

            Assert.Contains(at.Path + " is not at " + commit + " unchanged, and git cannot remove it.", error.Message, StringComparison.Ordinal);
            Assert.Contains("delete the folder", error.Remedy, StringComparison.Ordinal);
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path.Combine(at.Path, "held"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    /// <summary>A tag object is peeled to the commit it tags: the worktree stands at that commit, and is named for it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void At_resolves_an_annotated_tag_to_its_commit()
    {
        var commit = scratch.Commit("first", ("a.sql", "SELECT 1;\n"));
        scratch.Git("tag", "-a", "v1", "-m", "release 1");
        Assert.NotEqual(commit, scratch.Git("rev-parse", "v1"));   // the tag object, not the commit

        var at = Ok(Git.At(scratch.Root, "v1"));

        Assert.Equal(new Git.Worktree(Path.Combine(scratch.Root, ".dbchange", "worktrees", commit), commit), at);
    }

    /// <summary>
    /// A worktree's users hold .dbchange/worktrees/&lt;commit&gt;.lock shared while they run: another dbchange process took one worktree and has
    /// exited, this process holds the other, and the sweep removes the first with its lock file and keeps the second, reading no process id.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_sweep_removes_a_worktree_whose_holder_process_has_ended_and_keeps_one_a_running_process_holds()
    {
        var left = scratch.Commit("left", ("a.sql", "SELECT 2;\n"));
        var held = Ok(Git.At(scratch.Root, scratch.Commit("held", ("a.sql", "SELECT 1;\n"))));
        var (taken, _, _) = DbChangeProcess.AtOnce(scratch.Root, [left], 0).Single();   // taken by a process that has since exited
        var folder = Path.GetDirectoryName(taken.Path)!;

        Assert.Equal([left], Ok(Git.Sweep(scratch.Root)));

        Assert.True(Directory.Exists(held.Path));
        Assert.False(Directory.Exists(taken.Path));
        Assert.Equal([held.Commit + ".lock"], Directory.GetFiles(folder, "*.lock").Select(Path.GetFileName));
        Assert.Equal(2, scratch.Worktrees().Count);
        Assert.DoesNotContain(scratch.Worktrees(), w => w.EndsWith(left, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    public async Task At_and_a_sweep_wait_while_another_dbchange_process_holds_the_worktrees_lock_and_go_on_when_it_ends()
    {
        var first = Ok(Git.At(scratch.Root, scratch.Commit("first", ("a.sql", "SELECT 1;\n"))));
        var turn = Ok(FileLock.Take(new LocalState(scratch.Root).WorktreesLock, TimeSpan.Zero));

        var (at, sweep) = (Task.Run(() => Git.At(scratch.Root, first.Commit)), Task.Run(() => Git.Sweep(scratch.Root)));
        await Task.Delay(TimeSpan.FromSeconds(1));
        var waited = !at.IsCompleted && !sweep.IsCompleted;
        turn.Dispose();

        Assert.True(waited, "At or a sweep went on while another dbchange process held the worktrees lock");
        Assert.Equal(first, Ok(await at));
        Assert.Empty(Ok(await sweep));
    }

    /// <summary>Git for Windows refuses a path past 260 characters unless core.longpaths is on, which every command io/Git runs sets; the commit is made with plumbing so the scratch checkout never holds the path itself.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_commit_holding_a_path_longer_than_260_characters_checks_out()
    {
        scratch.Commit("first", ("a.sql", "SELECT 1;\n"));
        var path = string.Join('/', Enumerable.Repeat(new string('d', 60), 5)) + "/long.sql";
        var blob = scratch.Git("hash-object", "-w", "a.sql");
        scratch.Git("update-index", "--add", "--cacheinfo", "100644," + blob + "," + path);
        var commit = scratch.Git("commit-tree", scratch.Git("write-tree"), "-p", "HEAD", "-m", "a long path");
        scratch.Git("update-ref", "refs/heads/long", commit);
        scratch.Git("reset", "-q");

        var at = Ok(Git.At(scratch.Root, "long"));

        Assert.True(Path.Combine(at.Path, path).Length > 260, "the path is not past 260 characters");
        Assert.True(File.Exists(Path.Combine(at.Path, path)), "the long path was not checked out");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void MergeBase_finds_where_a_branch_left_main_and_ChangedPaths_lists_the_paths_two_refs_differ_in_from_the_root_in_ordinal_order()
    {
        var fork = scratch.Commit("fork", ("README.md", "dbchange\n"), ("dbo/Tables/Customer.sql", "c\n"), ("dbo/Tables/Order.sql", "o\n"));
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

    /// <summary>
    /// A clone with --depth 1 of two branches holds neither's history to their fork, so merge-base exits 1 as it does for unrelated
    /// histories; the answer names the shallow clone, since the histories do meet beyond what the clone holds.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void MergeBase_in_a_shallow_clone_that_cannot_see_the_fork_is_git_shallow_clone_and_two_unrelated_refs_are_ref_unrelated()
    {
        var origin = scratch.Origin();
        scratch.Commit("fork", ("a.sql", "SELECT 1;\n"));
        scratch.Git("switch", "-q", "-c", "feature");
        scratch.Commit("feature one", ("b.sql", "SELECT 2;\n"));
        scratch.Commit("feature two", ("b.sql", "SELECT 3;\n"));
        scratch.Git("switch", "-q", "main");
        scratch.Commit("main one", ("a.sql", "SELECT 4;\n"));
        scratch.Commit("main two", ("a.sql", "SELECT 5;\n"));
        scratch.Git("push", "-q", "origin", "main", "feature");
        var clone = scratch.Clone("--depth", "1", "--branch", "main", "file://" + origin.Replace('\\', '/'));
        scratch.GitAt(clone, "fetch", "-q", "--depth", "1", "origin", "feature:feature");
        scratch.Git("switch", "-q", "--orphan", "unrelated");
        scratch.Commit("unrelated", ("c.sql", "SELECT 6;\n"));

        var shallow = Failed(Git.MergeBase(clone, "main", "feature"), "git.shallow-clone");
        Failed(Git.MergeBase(scratch.Root, "main", "unrelated"), "ref.unrelated");

        Assert.Contains("--unshallow", shallow.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void CommitAndPush_publishes_exactly_the_named_branch_with_exactly_the_given_paths_and_leaves_the_caller_s_checkout_as_it_was()
    {
        var origin = scratch.Origin();
        var head = scratch.Commit("the estate", ("dbchange/evidence.shape.json", "{}\n"), ("README.md", "dbchange\n"));
        scratch.Git("push", "-q", "origin", "main");
        scratch.Write(("dbchange/evidence.shape.json", "{ \"sites\": [] }\n"), ("dbchange/ledgers/row-tiers.md", "| table | tier |\n"), ("README.md", "estate, edited\n"));
        scratch.Git("add", "README.md");   // the caller's own staged change stays theirs

        var pushed = Ok(Git.CommitAndPush(scratch.Root, ["dbchange/evidence.shape.json", "dbchange/ledgers/row-tiers.md"], "profile: dev's evidence", "dbchange/evidence-dev"));

        Assert.Equal("dbchange/evidence-dev", pushed.Branch);
        Assert.Equal(["refs/heads/dbchange/evidence-dev " + pushed.Commit, "refs/heads/main " + head], scratch.GitAt(origin, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads").Split('\n'));
        Assert.Equal(["dbchange/evidence.shape.json", "dbchange/ledgers/row-tiers.md"], scratch.GitAt(origin, "diff-tree", "-r", "--name-only", "--no-commit-id", head, pushed.Commit).Split('\n'));
        Assert.Equal(head + "\nDbChange Test\nprofile: dev's evidence", scratch.GitAt(origin, "log", "-1", "--format=%P%n%an%n%B", pushed.Commit));
        Assert.Equal("{ \"sites\": [] }", scratch.GitAt(origin, "show", pushed.Commit + ":dbchange/evidence.shape.json"));
        Assert.Equal(("refs/heads/main", head), (scratch.Git("symbolic-ref", "HEAD"), scratch.Git("rev-parse", "HEAD")));
        Assert.Equal(["M  README.md", " M dbchange/evidence.shape.json", "?? dbchange/ledgers/"], scratch.Git("status", "--porcelain").Split('\n'));
        Assert.Empty(Directory.GetFiles(new LocalState(scratch.Root).Temporary));   // the commit's own index is gone
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    public void A_branch_that_exists_or_git_does_not_take_is_refused_and_an_origin_that_does_not_answer_is_unreachable_with_no_credential_printed_and_no_branch_left()
    {
        scratch.Origin();
        scratch.Commit("the estate", ("dbchange/evidence.shape.json", "{}\n"));
        scratch.Write(("dbchange/evidence.shape.json", "{ }\n"));
        string[] paths = ["dbchange/evidence.shape.json"];
        Ok(Git.CommitAndPush(scratch.Root, paths, "first", "dbchange/evidence"));

        Failed(Git.CommitAndPush(scratch.Root, paths, "again", "dbchange/evidence"), "git-branch.exists");
        scratch.Git("branch", "-q", "-D", "dbchange/evidence");
        var existingAtOrigin = Failed(Git.CommitAndPush(scratch.Root, paths, "again", "dbchange/evidence"), "git-branch.exists");
        Failed(Git.CommitAndPush(scratch.Root, paths, "again", "dbchange/..evidence"), "git-branch.malformed");
        scratch.Git("remote", "set-url", "origin", "https://estate:s3cret-token@127.0.0.1:9/estate.git");
        var silent = Failed(Git.CommitAndPush(scratch.Root, paths, "again", "dbchange/elsewhere"), "origin.unreachable");

        Assert.Contains("the origin", existingAtOrigin.Message, StringComparison.Ordinal);
        new PlantedValue("s3cret-token").AbsentFrom(silent);
        Assert.Equal("", scratch.Git("branch", "--list", "dbchange/elsewhere", "dbchange/..evidence"));
        Assert.Equal("", scratch.Git("diff", "--cached", "--name-only"));
    }

    /// <summary>The origin's pre-receive hook refuses the branch: git push --porcelain reports [remote rejected] with the hook's reason, and the local branch is deleted.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_push_a_server_hook_refuses_is_origin_rejected_quoting_the_hook_and_leaves_no_branch()
    {
        var origin = scratch.Origin();
        var hook = Path.Combine(origin, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\necho 'branch names are reviewed first' >&2\nexit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        scratch.Commit("the estate", ("dbchange/evidence.shape.json", "{}\n"));
        scratch.Write(("dbchange/evidence.shape.json", "{ }\n"));

        var rejected = Failed(Git.CommitAndPush(scratch.Root, ["dbchange/evidence.shape.json"], "evidence", "dbchange/evidence"), "origin.rejected");

        Assert.Contains("pre-receive hook declined", rejected.Message, StringComparison.Ordinal);
        Assert.Equal("", scratch.Git("branch", "--list", "dbchange/evidence"));
        Assert.Equal("", scratch.GitAt(origin, "for-each-ref", "refs/heads"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_repository_with_no_origin_is_git_no_origin_before_anything_is_committed()
    {
        scratch.Commit("the estate", ("dbchange/evidence.shape.json", "{}\n"));
        scratch.Write(("dbchange/evidence.shape.json", "{ }\n"));

        var error = Failed(Git.CommitAndPush(scratch.Root, ["dbchange/evidence.shape.json"], "evidence", "dbchange/evidence"), "git.no-origin");

        Assert.Contains("git remote add origin", error.Remedy, StringComparison.Ordinal);
        Assert.Equal("", scratch.Git("branch", "--list", "dbchange/evidence"));
    }

    /// <summary>
    /// A push that meets a branch created at the origin between ls-remote and push prints the measured porcelain line
    /// "!\tHEAD:refs/heads/x\t[rejected] (stale info)" and exits 1: git-branch.exists naming the origin, with the local branch deleted.
    /// A push interrupted by the run's cancellation leaves no local branch either.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_branch_the_origin_gained_during_the_push_is_git_branch_exists_and_an_interrupted_push_leaves_no_local_branch()
    {
        scratch.Origin();
        scratch.Commit("the estate", ("dbchange/evidence.shape.json", "{}\n"));
        scratch.Write(("dbchange/evidence.shape.json", "{ }\n"));
        string[] paths = ["dbchange/evidence.shape.json"];
        Ran Stale(Command c, CancellationToken t) => c.Arguments.Contains("ls-remote") ? new Ran.Exited(2, "", "")
            : c.Arguments.Contains("push") ? new Ran.Exited(1, "To origin\n!\tHEAD:refs/heads/dbchange/evidence\t[rejected] (stale info)\nDone\n", "")
            : Command.Run(c, t);
        Ran Interrupted(Command c, CancellationToken t) => c.Arguments.Contains("push") ? throw new OperationCanceledException() : Command.Run(c, t);

        var gained = Failed(Git.CommitAndPush(scratch.Root, paths, "evidence", "dbchange/evidence", Stale), "git-branch.exists");
        Assert.Throws<OperationCanceledException>(() => Git.CommitAndPush(scratch.Root, paths, "evidence", "dbchange/evidence", Interrupted));

        Assert.Contains("appeared there while dbchange pushed", gained.Message, StringComparison.Ordinal);
        Assert.Equal("", scratch.Git("branch", "--list", "dbchange/evidence"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_ref_that_names_no_commit_is_ref_unresolved_and_a_folder_that_is_no_repository_is_git_not_a_repository()
    {
        scratch.Commit("main", ("a.sql", "SELECT 1;\n"));

        var unresolved = Failed(Git.At(scratch.Root, "no-such-tag"), "ref.unresolved");
        Failed(Git.ChangedPaths(Path.Combine(scratch.Root, "no-such-folder"), "main", "HEAD"), "git.not-a-repository");

        Assert.Contains("'no-such-tag'", unresolved.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, ".dbchange", "worktrees")) && Directory.EnumerateDirectories(Path.Combine(scratch.Root, ".dbchange", "worktrees")).Any());
    }

    /// <summary>
    /// Exit 1 is the one answer rev-parse --verify --quiet gives for a ref that names no commit; any other exit is git's own failure, such
    /// as a git older than 2.24 refusing --end-of-options, and reads as git.failed quoting git, never as "names no commit". A git that
    /// exits without writing an error, as one killed by a signal does, is named by its exit code.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_git_that_fails_to_resolve_a_ref_is_git_failed_quoting_git_or_naming_the_exit_code_when_git_wrote_nothing()
    {
        scratch.Commit("main", ("a.sql", "SELECT 1;\n"));
        Ran Old(Command c, CancellationToken t) => c.Arguments.Contains("--verify") ? new Ran.Exited(129, "", "error: unknown option `end-of-options'\n") : Command.Run(c, t);
        Ran Killed(Command c, CancellationToken t) => c.Arguments.Contains("--verify") ? new Ran.Exited(137, "", "") : Command.Run(c, t);

        var old = Failed(Git.At(scratch.Root, "main", Old));
        var killed = Failed(Git.At(scratch.Root, "main", Killed));

        Assert.Equal(("git.failed", "git rev-parse failed: error: unknown option `end-of-options'"), (old.Code, old.Message));
        Assert.Equal(("git.failed", "git rev-parse exited 137 and wrote no error."), (killed.Code, killed.Message));
    }

    /// <summary>
    /// A machine with no git identity anywhere, as a CI runner is: commit-tree exits 128 with "Author identity unknown" (read from a
    /// stand-in, since the scratch repository sets its own identity). It is git.no-identity, naming the repository and the two settings,
    /// where it was git.failed with the remedy "Fix what git names"; the branch is never made.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_commit_with_no_git_identity_anywhere_is_git_no_identity_naming_the_repository_and_makes_no_branch()
    {
        scratch.Origin();
        scratch.Commit("the estate", ("dbchange/evidence.shape.json", "{}\n"));
        scratch.Write(("dbchange/evidence.shape.json", "{ }\n"));
        Ran NoIdentity(Command c, CancellationToken t) => c.Arguments.Contains("commit-tree")
            ? new Ran.Exited(128, "", "Author identity unknown\n\n*** Please tell me who you are.\n\nRun\n\n  git config --global user.email \"you@example.com\"\n")
            : Command.Run(c, t);

        var error = Failed(Git.CommitAndPush(scratch.Root, ["dbchange/evidence.shape.json"], "evidence", "dbchange/evidence", NoIdentity), "git.no-identity");

        Assert.Equal("git commit-tree needs a committer's name and email, and git has none configured for " + scratch.Root + ".", error.Message);
        Assert.Contains("git config user.name", error.Remedy, StringComparison.Ordinal);
        Assert.Equal("", scratch.Git("branch", "--list", "dbchange/evidence"));
    }

    /// <summary>
    /// N14 of the pre-M2 review: a git that times out inside a worktree dbchange keeps is git.timed-out, and the worktree stays. Git.At once
    /// read any failure there as a worktree that is not current, removed it and made it again, and the new one met the same failure.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_git_that_times_out_inside_a_kept_worktree_is_git_timed_out_and_the_worktree_stays()
    {
        var commit = scratch.Commit("first", ("a.sql", "SELECT 1;\n"));
        var worktree = Ok(Git.At(scratch.Root, commit)).Path;
        Ran Stalls(Command c, CancellationToken t) => c.Arguments.Contains(worktree) && c.Arguments.Contains("rev-parse") ? new Ran.TimedOut(c.Timeout, "", "") : Command.Run(c, t);

        Failed(Git.At(scratch.Root, commit, Stalls), "git.timed-out");

        Assert.True(File.Exists(Path.Combine(worktree, "a.sql")), "the worktree was removed");
        Assert.Equal(worktree, Ok(Git.At(scratch.Root, commit)).Path);
    }

    /// <summary>git 2.35.2's safe.directory refusal, as git prints it, read from a stand-in since this machine's git predates it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_repository_git_refuses_for_its_owner_is_git_dubious_ownership_naming_git_s_command()
    {
        const string Refusal = "fatal: detected dubious ownership in repository at 'C:/share/estate'\nTo add an exception for this directory, call:\n\n\tgit config --global --add safe.directory C:/share/estate\n";
        Ran Refuses(Command c, CancellationToken t) => new Ran.Exited(128, "", Refusal);

        var error = Failed(Git.At(scratch.Root, "HEAD", Refuses), "git.dubious-ownership");

        Assert.Contains("C:/share/estate", error.Message, StringComparison.Ordinal);
        Assert.Contains("git config --global --add safe.directory C:/share/estate", error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_git_that_does_not_run_is_git_missing_and_one_past_its_timeout_is_git_timed_out_or_origin_unreachable_for_the_origin_s_commands()
    {
        scratch.Origin();
        scratch.Commit("the estate", ("dbchange/evidence.shape.json", "{}\n"));
        Ran Missing(Command c, CancellationToken t) => new Ran.NotFound(c.Program, "'git' is on no folder of the PATH.");
        Ran Hangs(Command c, CancellationToken t) => new Ran.TimedOut(c.Timeout, "", "");
        Ran OriginHangs(Command c, CancellationToken t) => c.Arguments.Contains("ls-remote") ? new Ran.TimedOut(c.Timeout, "", "") : Command.Run(c, t);

        Failed(Git.At(scratch.Root, "HEAD", Missing), "git.missing");
        var hung = Failed(Git.At(scratch.Root, "HEAD", Hangs), "git.timed-out");
        var unreachable = Failed(Git.CommitAndPush(scratch.Root, ["dbchange/evidence.shape.json"], "evidence", "dbchange/evidence", OriginHangs), "origin.unreachable");

        Assert.Contains("5 minutes", hung.Message, StringComparison.Ordinal);
        Assert.Contains("did not answer in 5 minutes", unreachable.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The command io/Git builds removes every variable the installed git lists as repository-local (git rev-parse --local-env-vars: GIT_DIR,
    /// GIT_INDEX_FILE, GIT_OBJECT_DIRECTORY, GIT_CONFIG_PARAMETERS and the rest), GIT_CEILING_DIRECTORIES, GIT_NAMESPACE, every GIT_TRACE
    /// variable the process holds, and the two gettext reads, and sets LC_ALL=C; every command runs with core.longpaths on and withholds
    /// DBCHANGE_SQL. The list is compared with git's own, so a git that names a new variable fails this test.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_command_Git_builds_removes_every_variable_git_rev_parse_local_env_vars_lists_and_every_GIT_TRACE_variable()
    {
        var listed = scratch.Git("rev-parse", "--local-env-vars").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Command? built = null;
        Ran Records(Command c, CancellationToken t)
        {
            built ??= c;
            return new Ran.Exited(2, "", "stand-in");
        }

        Environment.SetEnvironmentVariable("GIT_TRACE_PROBE", "1");
        try
        {
            Failed(Git.HoldingOf(scratch.Root, Path.Combine(scratch.Root, "dev.connection"), Records));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_TRACE_PROBE", null);
        }

        Assert.NotNull(built);
        Assert.NotEmpty(listed);
        Assert.All(listed.Concat(["GIT_CEILING_DIRECTORIES", "GIT_NAMESPACE", "GIT_TRACE_PROBE", "LANGUAGE", "LC_MESSAGES", "DBCHANGE_SQL"]), variable => Assert.True(built.Environment.TryGetValue(variable, out var value) && value is null, variable + " reaches git"));
        Assert.Equal(("C", "0", "1"), (built.Environment["LC_ALL"], built.Environment["GIT_TERMINAL_PROMPT"], built.Environment["GIT_LFS_SKIP_SMUDGE"]));
        Assert.Equal(["-c", "core.longpaths=true", "-C", scratch.Root, "rev-parse", "--show-toplevel"], built.Arguments);
        Assert.Equal(("git", Git.Timeout), (built.Program, built.Timeout));
    }

    /// <summary>
    /// io/Git reads git's English "not a git repository" to tell a folder in no repository from a failed search, so git runs with
    /// LC_ALL=C and without LANGUAGE and LC_MESSAGES, which GNU gettext would otherwise read to choose a translation; and it runs with
    /// the terminal prompt off and Git LFS's smudge off (VALUES.md X5), so no push waits on a prompt and no pointer is fetched. A stand-in
    /// for git prints the environment it is given to its error stream and exits 2, so HoldingOf fails with git.failed and quotes that
    /// environment: LC_ALL=C and the two settings are in it, and LANGUAGE and LC_MESSAGES, set in this process, are not.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X5")]
    public void Git_runs_with_LC_ALL_C_and_without_the_caller_s_LANGUAGE_and_LC_MESSAGES()
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

        Ran StandIn(Command c, CancellationToken t) => Command.Run(c with { Program = git }, t);
        var asked = (Language: Environment.GetEnvironmentVariable("LANGUAGE"), Messages: Environment.GetEnvironmentVariable("LC_MESSAGES"));
        Environment.SetEnvironmentVariable("LANGUAGE", "de");
        Environment.SetEnvironmentVariable("LC_MESSAGES", "de_DE.UTF-8");
        Error error;
        try
        {
            error = Failed(Git.HoldingOf(scratch.Root, Path.Combine(scratch.Root, "dev.connection"), StandIn));
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
        Assert.Contains("GIT_TERMINAL_PROMPT=0", variables);
        Assert.Contains("GIT_LFS_SKIP_SMUDGE=1", variables);
        Assert.DoesNotContain(variables, line => line.StartsWith("LANGUAGE=", StringComparison.OrdinalIgnoreCase) || line.StartsWith("LC_MESSAGES=", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Expect.Value and Expect.Failed under the names the classes that read a ref's worktree beside this one use.</summary>
    internal static T Ok<T>(Result<T> result) => Expect.Value(result);

    internal static Error Failed<T>(Result<T> result) => Expect.Failed(result);

    internal static Error Failed<T>(Result<T> result, string code) => Expect.Failed(result, code);
}

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
    /// exist, built at the commit, builds the commit's project alone. Without the stop files .dbchange/worktrees/ carries, the build fails with MSB4019.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_ref_s_build_reads_its_own_Directory_Build_props_alone_and_never_the_enclosing_checkout_s()
    {
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var file in (string[])[Project, "classic-minimal/ClassicMinimal.refactorlog", "classic-minimal/Script.PostDeployment.sql", "classic-minimal/dbo/Tables/Customer.sql"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch.Root, file))!);
            File.Copy(Path.Combine(golden, file), Path.Combine(scratch.Root, file));
        }

        var commit = scratch.Commit("classic-minimal without MSBuild files", (".gitignore", ".dbchange/\n"));
        scratch.Write(("Directory.Build.props", "<Project><Import Project=\"does-not-exist.targets\" /></Project>\n"));

        var dacpac = GitTests.Ok(Ssdt.Build(GitTests.Ok(Git.At(scratch.Root, commit)), Project, tool.Folder, Output));

        Assert.Equal(["[dbo].[Customer].[GivenName]", "[dbo].[Customer].[Id]"], Columns(dacpac.Path));
    }

    /// <summary>One round of dbchange processes, one per commit in the order released, a gap apart; each commit's worktree stands at it when all have exited.</summary>
    private List<(Git.Worktree At, long Took, string Dacpac)> Round(string[] commits, long gap, params string[] build)
    {
        var taken = DbChangeProcess.AtOnce(scratch.Root, commits, gap, build);
        foreach (var (at, _, _) in taken)
        {
            Assert.True(File.Exists(Path.Combine(at.Path, Project)), "released " + gap + " ms apart, " + at.Path + " was swept from under the dbchange process that took it");
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
        Assert.Equal([Path.Combine(scratch.Root, ".dbchange", "worktrees", before), Path.Combine(scratch.Root, ".dbchange", "worktrees", after)], built.Select(b => b.At.Path));
        Assert.Equal([Path.Combine(Output, before), Path.Combine(Output, after)], built.Select(b => Path.GetDirectoryName(Path.GetDirectoryName(b.Dacpac))));
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
        return package.Model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass).SelectMany(t => t.GetReferenced(Table.Columns)).Select(c => c.Name.ToString()).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every file under a folder, its bytes read as Latin-1 so any path it records can be searched for.</summary>
    private static IEnumerable<string> Bytes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Encoding.Latin1.GetString(File.ReadAllBytes(f)));
}

/// <summary>
/// A git repository in a folder of its own under the temporary folder, outside every checkout: its user, email, signing
/// and line endings set in it alone, git told never to look above it, and its root checked before any test runs git in it.
/// Disposing it releases this process's holds on its worktrees, then deletes it.
/// </summary>
internal sealed class Scratch : IDisposable
{
    private readonly ScratchFolder folder = ScratchFolder.Temporary("git");

    private string Parent => folder.Path;

    public Scratch()
    {
        var root = Path.Combine(Parent, "repository");
        Run(Parent, "init", "-q", "--initial-branch=main", root);
        Root = Path.GetFullPath(Run(root, "rev-parse", "--show-toplevel"));
        Assert.Equal(root, Root, ignoreCase: true);   // git found this repository, and no other
        foreach (var (key, value) in ((string, string)[])[("user.name", "DbChange Test"), ("user.email", "dbchange-test@example.invalid"), ("commit.gpgsign", "false"), ("core.autocrlf", "false")])
        {
            Git("config", key, value);
        }
    }

    public string Root { get; }

    public string Git(params string[] arguments) => Run(Root, arguments);

    /// <summary>git in a folder of this scratch: a worktree of the repository, the origin, or a clone.</summary>
    public string GitAt(string directory, params string[] arguments)
    {
        Assert.StartsWith(Parent, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
        return Run(directory, arguments);
    }

    /// <summary>A bare repository beside this one, added as its origin.</summary>
    public string Origin()
    {
        var origin = Path.Combine(Parent, "origin.git");
        Run(Parent, "init", "-q", "--bare", origin);
        Git("remote", "add", "origin", origin);
        return origin;
    }

    /// <summary>A clone beside this repository, made with the options given before the URL; its root.</summary>
    public string Clone(params string[] arguments)
    {
        var clone = Path.Combine(Parent, "clone");
        Run(Parent, ["clone", "-q", .. arguments, clone]);
        return clone;
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
        Io.Git.Release(Root);
        folder.Dispose();
    }

    private string Run(string directory, params string[] arguments) => Programs.TestGit(directory, Parent, arguments);
}
