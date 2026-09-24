using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Estate.Kernel;

namespace Estate.Io;

/// <summary>
/// Git, the only store (V3_MILESTONES.md §2.2, §4 row 5), through the git command line as the caller; the tool stores no
/// credential and withholds any a URL carries from the errors it quotes. A ref's worktree is .estate/worktrees/&lt;commit&gt;/,
/// held by one lock per estate process beside it (&lt;commit&gt;.&lt;pid&gt;.lock); every At first sweeps away each worktree no
/// running holder's lock names. At and the sweep run only in the worktrees' turn (Turn), one estate process at a time, so no
/// sweep reads the locks between another process's lock and its worktree. Builds write under .estate/build/&lt;commit&gt;/,
/// never in a worktree, so one serves them all.
/// </summary>
public static class Git
{
    /// <summary>A ref's commit, checked out detached at Path.</summary>
    public sealed record Worktree(string Path, string Commit);

    /// <summary>What a push published: the new branch and its one commit.</summary>
    public sealed record Pushed(string Branch, string Commit);

    /// <summary>How long At or a sweep waits for another estate process's turn before the sharing error surfaces.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    private static readonly Regex Credential = new(@"(?<=://)[^/\s@]+@", RegexOptions.CultureInvariant);

    /// <summary>The worktree of the commit a ref names, made when absent, reused when present, and held by this process.</summary>
    public static Result<Worktree> At(string repository, string reference) => Root(repository).Bind(root => Resolve(root, reference).Bind(commit =>
    {
        using var turn = Turn(root);
        var path = Path.Combine(root, ".estate", "worktrees", commit);
        Write.Text(path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".lock", commit + "\n");   // held before the sweep looks
        var current = File.Exists(Path.Combine(path, ".git")) && Step(path, ["rev-parse", "HEAD"]) is Result<string>.Ok { Value: var head } && head == commit;
        return Swept(root).Bind(_ =>
            current ? Result.Ok(new Worktree(path, commit))
            : Directory.Exists(path) && !Removed(root, path) ? new Refusal("git.failed", path + " is not at " + commit + ", and git cannot remove it.", "delete the folder, then run estate again")
            : Step(root, ["worktree", "add", "--force", "--detach", path, commit]).Map(_ => new Worktree(path, commit)));
    }));

    /// <summary>Removes each worktree under .estate/worktrees/ that no running estate holds, and prunes git's records; the commits whose worktrees went.</summary>
    public static Result<IReadOnlyList<string>> Sweep(string repository) => Root(repository).Bind(root =>
    {
        using var turn = Turn(root);
        return Swept(root);
    });

    /// <summary>The commit where the histories of two refs meet.</summary>
    public static Result<string> MergeBase(string repository, string a, string b) => Root(repository).Bind(root => Resolve(root, a).Bind(first => Resolve(root, b).Bind<string>(second =>
        Run(root, ["merge-base", first, second]) switch
        {
            (0, var commit, _) => commit.Trim(),
            (1, _, _) => new Refusal("ref.unrelated", "'" + a + "' and '" + b + "' share no commit: their histories never meet.", "name two refs of one history, such as a branch and the branch it left"),
            (_, _, var errors) => Failed("merge-base", errors),
        })));

    /// <summary>The paths that differ between two refs' trees, from the repository's root, in ordinal order; a rename is both its paths.</summary>
    public static Result<IReadOnlyList<string>> ChangedPaths(string repository, string a, string b) => Root(repository).Bind(root => Resolve(root, a).Bind(from => Resolve(root, b).Bind(to =>
        Step(root, ["diff-tree", "-r", "-z", "--name-only", "--no-commit-id", from, to]).Map<IReadOnlyList<string>>(paths => paths.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList()))));

    /// <summary>
    /// A new branch holding one commit on HEAD, of HEAD's tree with the paths (from the repository's root) as the working tree
    /// has them, pushed to the origin. The caller's branch, index and working tree stay as they were; a branch that exists
    /// here or at the origin is refused before anything is written, and a push that fails leaves no branch behind.
    /// </summary>
    public static Result<Pushed> CommitAndPush(string repository, IReadOnlyList<string> paths, string message, string branch) => Root(repository).Bind<Pushed>(root =>
    {
        var name = "refs/heads/" + branch;
        if (Run(root, ["check-ref-format", name]).Exit != 0)
        {
            return new Refusal("branch.malformed", "'" + branch + "' is not a name git takes for a branch.", "name the branch in words joined by '-' and '/', such as estate/evidence-dev");
        }

        var here = Run(root, ["rev-parse", "--verify", "--quiet", name]).Exit == 0;
        return (here ? (0, "", "") : Run(root, ["ls-remote", "--exit-code", "origin", name])) switch   // exit 2: the origin has no such branch
        {
            (2, _, _) => Committed(root, paths, message).Bind(commit => Step(root, ["update-ref", name, commit, ""]).Bind<Pushed>(_ =>
            {
                if (Run(root, ["push", "--quiet", "--force-with-lease=" + name + ":", "origin", name + ":" + name]) is (not 0, _, var refused))
                {
                    Run(root, ["update-ref", "-d", name, commit]);
                    return Unreachable("push", refused);
                }

                return new Pushed(branch, commit);
            })),
            (0, _, _) => new Refusal(
                "branch.taken", "The branch " + branch + " already exists " + (here ? "in this repository." : "at the origin."), "name a new branch, or delete " + branch + " where it exists once its review is done"),
            (_, _, var errors) => Unreachable("ls-remote", errors),
        };
    });

    /// <summary>The commit of HEAD's tree with the paths added, built in an index of its own so the caller's is never touched.</summary>
    private static Result<string> Committed(string root, IReadOnlyList<string> paths, string message)
    {
        var index = Path.Combine(Path.GetTempPath(), "estate-index-" + Path.GetRandomFileName());
        var commit = Resolve(root, "HEAD").Bind(head => Step(root, ["read-tree", head], index).Bind(_ => Step(root, ["add", "--", .. paths], index))
            .Bind(_ => Step(root, ["write-tree"], index)).Bind(tree => Step(root, ["commit-tree", tree, "-p", head, "-m", message])));
        File.Delete(index);
        return commit;
    }

    /// <summary>
    /// The worktrees' turn: .estate/worktrees/.turn opened for this process alone. Another estate process's At or sweep waits
    /// for it, from this process or any other; the system closes it when its holder ends, however the holder ends.
    /// </summary>
    private static FileStream Turn(string root)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(root, ".estate", "worktrees")).FullName, ".turn");
        for (var waiting = Stopwatch.StartNew(); ; Thread.Sleep(20))
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException sharing) when (sharing.GetType() == typeof(IOException) && waiting.Elapsed < Patience)
            {
            }
        }
    }

    /// <summary>The sweep itself, in the caller's turn, whose folder it is: the locks it reads stay true until it has removed and pruned.</summary>
    private static Result<IReadOnlyList<string>> Swept(string root)
    {
        var folder = new DirectoryInfo(Path.Combine(root, ".estate", "worktrees"));
        var worktrees = folder.GetDirectories();
        var processes = Process.GetProcesses();
        var running = processes.Select(p => p.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        Array.ForEach(processes, p => p.Dispose());
        var locks = folder.GetFiles("*.lock").Select(f => (File: f, Name: f.Name.Split('.'))).ToLookup(l => running.Contains(l.Name[1]));
        locks[false].ToList().ForEach(l => l.File.Delete());
        var removed = worktrees.Where(w => !locks[true].Any(l => l.Name[0] == w.Name) && Removed(root, w.FullName)).Select(w => w.Name).Order(StringComparer.Ordinal).ToList();
        return Step(root, ["worktree", "prune"]).Map<IReadOnlyList<string>>(_ => removed);
    }

    /// <summary>Removes a worktree and git's record of it; false when git cannot, such as while something holds a file open.</summary>
    private static bool Removed(string root, string path) => Run(root, ["worktree", "remove", "--force", "--force", path]).Exit == 0;

    private static Result<string> Root(string repository) => Run(repository, ["rev-parse", "--show-toplevel"]) switch
    {
        (0, var root, _) => Path.GetFullPath(root.Trim()),
        (-1, _, _) => new Refusal("git.missing", "git does not run here: it is not installed or not on the PATH.", "install git and put it on the PATH; then estate doctor"),
        (_, _, var errors) => new Refusal("git.not-a-repository", repository + " is not in a git repository: " + errors.Trim(), "run estate in a clone of the repository, or name the clone's folder"),
    };

    private static Result<string> Resolve(string root, string reference) => Run(root, ["rev-parse", "--verify", "--quiet", "--end-of-options", reference + "^{commit}"]) is (0, var commit, _)
        ? commit.Trim()
        : new Refusal("ref.unresolved", "'" + reference + "' names no commit in " + root + ".", "name a branch, tag or commit the repository holds; git fetch brings the origin's");

    private static Refusal Unreachable(string command, string errors) => new Refusal(
        "origin.unreachable", "git " + command + " to the origin failed, and nothing was committed: " + errors.Trim(),
        "check that git fetch reaches the origin, signing in through git's own credential helper when it asks; then run estate again");

    private static Refusal Failed(string command, string errors) => new Refusal("git.failed", "git " + command + " failed: " + errors.Trim(), "fix what git names, then run estate again");

    /// <summary>git that must succeed: its output less the final line break; a failure quotes git's error.</summary>
    private static Result<string> Step(string directory, IReadOnlyList<string> arguments, string? index = null) => Run(directory, arguments, index) switch
    {
        (0, var output, _) => output.TrimEnd('\n'),
        (_, _, var errors) => Failed(arguments[0], errors),
    };

    /// <summary>
    /// git -C the directory, as the caller: its exit (-1 when git does not start), output, and errors less any credential in a
    /// URL. The caller's GIT_DIR and index never reach it, it never prompts on the terminal, and LFS pointers stay unfetched.
    /// </summary>
    private static (int Exit, string Output, string Errors) Run(string directory, IReadOnlyList<string> arguments, string? index = null)
    {
        var start = new ProcessStartInfo("git", ["-C", directory, .. arguments])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var variable in (string[])["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE"])
        {
            start.Environment.Remove(variable);
        }

        (start.Environment["GIT_TERMINAL_PROMPT"], start.Environment["GIT_LFS_SKIP_SMUDGE"]) = ("0", "1");
        if (index is not null)
        {
            start.Environment["GIT_INDEX_FILE"] = index;
        }

        try
        {
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output, Credential.Replace(errors.Result, ""));
        }
        catch (Win32Exception)
        {
            return (-1, "", "");
        }
    }
}
