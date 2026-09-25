using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Estate.Kernel;

namespace Estate.Io;

/// <summary>
/// Git, the only store (V3_MILESTONES.md §2.2, §4 row 5), through the git command line as the caller, run through io/Command, with git's
/// whole surface mapped here: the tool stores no credential and withholds any a URL carries from the errors it quotes; git's exit codes
/// and messages become typed answers or named errors (ref.unresolved for exit 1 of rev-parse --verify alone; git.not-a-repository and
/// git.dubious-ownership from git's own messages; git.shallow-clone when merge-base fails in a clone that holds part of the history;
/// git.no-origin, origin.denied, origin.rejected and git-branch.exists from git push --porcelain and git's errors; git.missing when no
/// git runs; git.timed-out after five minutes, or origin.unreachable for ls-remote and push, which wait on the origin and a person's
/// sign-in; otherwise git.failed quoting git, or naming the exit code when git wrote nothing). A ref's worktree is
/// .estate/worktrees/&lt;commit&gt;/ under the estate's root (LocalState): every process using it holds .estate/worktrees/&lt;commit&gt;.lock
/// shared while it runs, and a sweep removes a worktree only after taking that lock exclusively, so no process id is read and two estates
/// in different PID namespaces on one checkout never remove each other's. At and the sweep run under .estate/worktrees.lock
/// (FileLock), one estate process at a time. A worktree is reused only while it stands at its commit with nothing changed or added,
/// since a build reads what the folder holds; otherwise it is made again. Builds write under .estate/build/, never in a worktree, so one
/// serves them all. Every command runs with core.longpaths on, which Git for Windows reads for a path past 260 characters; git's messages
/// are English (LC_ALL=C), git never prompts on the terminal, and Git LFS pointers stay unfetched.
/// </summary>
public static class Git
{
    /// <summary>A ref's commit, checked out detached at Path.</summary>
    public sealed record Worktree(string Path, string Commit);

    /// <summary>What a push published: the new branch and its one commit.</summary>
    public sealed record Pushed(string Branch, string Commit);

    /// <summary>How long a git command may run before estate stops it: a checkout of a large estate takes seconds, and a person signing in through a credential manager window a minute or two.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>How long At or a sweep waits for the worktrees lock, which is held across a sweep and a checkout.</summary>
    public static readonly TimeSpan WorktreesTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The variables removed from git's environment, so the caller's shell, hook or alias never selects another repository, index,
    /// object store or configuration for it, and no translation or trace line enters a quoted error: every variable git rev-parse
    /// --local-env-vars lists (git 2.31.1's sixteen), GIT_CEILING_DIRECTORIES, GIT_NAMESPACE, and LANGUAGE and LC_MESSAGES, which GNU
    /// gettext reads to choose a translation. Every GIT_TRACE variable present is removed too, when the command is built.
    /// </summary>
    public static IReadOnlyList<string> Scrubbed { get; } =
    [
        "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_OBJECT_DIRECTORY", "GIT_DIR", "GIT_WORK_TREE",
        "GIT_IMPLICIT_WORK_TREE", "GIT_GRAFT_FILE", "GIT_INDEX_FILE", "GIT_NO_REPLACE_OBJECTS", "GIT_REPLACE_REF_BASE", "GIT_PREFIX", "GIT_INTERNAL_SUPER_PREFIX",
        "GIT_SHALLOW_FILE", "GIT_COMMON_DIR", "GIT_CEILING_DIRECTORIES", "GIT_NAMESPACE", "LANGUAGE", "LC_MESSAGES",
    ];

    /// <summary>How long the deletion of a half-pushed branch may take after a failed or interrupted push.</summary>
    private static readonly TimeSpan Cleanup = TimeSpan.FromSeconds(30);

    private static readonly Regex Credential = new(@"(?<=://)[^/\s@]+@", RegexOptions.CultureInvariant);

    /// <summary>The repository git refuses for its owner, as its message names it: at 'C:/share/estate'.</summary>
    private static readonly Regex Refused = new(@"repository at '(?<path>[^']+)'", RegexOptions.CultureInvariant);

    /// <summary>A push --porcelain line for a ref the origin refused: ! then the refspec, then [rejected] or [remote rejected] and the reason in parentheses.</summary>
    private static readonly Regex Rejected = new(@"^!\t[^\t]*\t\[(?<how>rejected|remote rejected)\] \((?<reason>[^)]*)\)", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>The worktree holders' locks this process has taken, kept until it ends, so no sweep removes a worktree it uses.</summary>
    private static readonly List<FileLock> Held = [];

    /// <summary>The worktree of the commit a ref names, made when absent, reused when present and unchanged, and held by this process; run from the estate's root, whose .estate/ holds it.</summary>
    public static Result<Worktree> At(string estateRoot, string reference, Runner? run = null, CancellationToken cancel = default)
    {
        var (git, state) = (run ?? Command.Run, new LocalState(estateRoot));
        return Root(git, estateRoot, cancel).Bind(root => Resolve(git, root, reference, cancel).Bind(commit => state.Made(state.Worktrees)
            .Bind(_ => FileLock.Take(state.WorktreesLock, WorktreesTimeout, cancel)).Bind<Worktree>(turn =>
            {
                using (turn)
                {
                    return FileLock.TakeShared(state.WorktreeHolders(commit), TimeSpan.Zero, cancel).Bind<Worktree>(holding =>
                    {
                        lock (Held)
                        {
                            Held.Add(holding);   // held before the sweep looks
                        }

                        var path = state.Worktree(commit);
                        return Swept(git, root, state, cancel).Bind<Worktree>(_ =>
                            Current(git, path, commit, cancel) ? new Worktree(path, commit)
                            : Directory.Exists(path) && !Removed(git, root, path, cancel) ? new Error("git.failed", path + " is not at " + commit + " unchanged, and git cannot remove it.",
                                "Close any program that holds a file under " + path + " open, delete the folder, and run estate again.")
                            : Step(git, root, ["worktree", "add", "--force", "--detach", path, commit], cancel).Map(_ => new Worktree(path, commit)));
                    });
                }
            })));
    }

    /// <summary>
    /// The holds this process keeps on the worktrees under an estate's root, released: a sweep may then remove them. Estate's own process
    /// ends without calling it; a host that outlives its runs (a test process) calls it before it deletes the checkout.
    /// </summary>
    public static void Release(string estateRoot)
    {
        var under = new LocalState(estateRoot).Worktrees;
        lock (Held)
        {
            foreach (var hold in Held.Where(h => h.Path.StartsWith(under, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList())
            {
                hold.Dispose();
                Held.Remove(hold);
            }
        }
    }

    /// <summary>Removes each worktree under .estate/worktrees/ that no running estate holds, and prunes git's records; the commits whose worktrees went.</summary>
    public static Result<IReadOnlyList<string>> Sweep(string estateRoot, Runner? run = null, CancellationToken cancel = default)
    {
        var (git, state) = (run ?? Command.Run, new LocalState(estateRoot));
        return Root(git, estateRoot, cancel).Bind(root => state.Made(state.Worktrees).Bind(_ => FileLock.Take(state.WorktreesLock, WorktreesTimeout, cancel)).Bind(turn =>
        {
            using (turn)
            {
                return Swept(git, root, state, cancel);
            }
        }));
    }

    /// <summary>The commit where the histories of two refs meet.</summary>
    public static Result<string> MergeBase(string repository, string a, string b, Runner? run = null, CancellationToken cancel = default)
    {
        var git = run ?? Command.Run;
        return Root(git, repository, cancel).Bind(root => Resolve(git, root, a, cancel).Bind(first => Resolve(git, root, b, cancel).Bind(second =>
            Answered(git, root, ["merge-base", first, second], cancel).Bind<string>(merge => merge.Code switch
            {
                0 => merge.Output.Trim(),
                1 when Shallow(git, root, cancel) => new Error("git.shallow-clone", "'" + a + "' and '" + b + "' share no commit in this clone, which holds only part of the history.",
                    "Fetch the whole history with git fetch --unshallow; in a pipeline, check out with full history (fetch depth 0)."),
                1 => new Error("ref.unrelated", "'" + a + "' and '" + b + "' share no commit: their histories never meet.", "Name two refs of one history, such as a branch and the branch it left."),
                _ => Failed("merge-base", merge),
            }))));
    }

    /// <summary>The paths that differ between two refs' trees, from the repository's root, in ordinal order; a rename is both its paths.</summary>
    public static Result<IReadOnlyList<string>> ChangedPaths(string repository, string a, string b, Runner? run = null, CancellationToken cancel = default)
    {
        var git = run ?? Command.Run;
        return Root(git, repository, cancel).Bind(root => Resolve(git, root, a, cancel).Bind(from => Resolve(git, root, b, cancel).Bind(to =>
            Step(git, root, ["diff-tree", "-r", "-z", "--name-only", "--no-commit-id", from, to], cancel)
                .Map<IReadOnlyList<string>>(paths => paths.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList()))));
    }

    /// <summary>
    /// How git holds the file a file: reference names, which must stay out of every commit, since a commit reaches every clone:
    /// Tracked, committed already; NotIgnored, so the next git add commits it; Ignored; or InNoRepository. EstateInNoRepository when
    /// the estate's root is in no git repository, where git cannot say what the estate's clones would commit.
    /// </summary>
    public enum Holding
    {
        Tracked,
        NotIgnored,
        Ignored,
        InNoRepository,
        EstateInNoRepository,
    }

    /// <summary>
    /// How git holds an existing <paramref name="file"/>, asked in the file's own folder, so the repository is the one git finds there
    /// (the estate's, another, or none), and by the name the caller gives, which io/SqlServer takes from the folder's listing
    /// (SqlServer.Listed), since git never sees another spelling Windows opens. A tracked file is Tracked whatever .gitignore lists. Where the file system opens a file
    /// whatever the case of its name (Windows, macOS, or core.ignorecase true), git's index is searched for the name without case, as
    /// .gitignore already is, so estate/Dev.connection finds a tracked estate/dev.connection. InNoRepository and EstateInNoRepository
    /// come only from git's own "not a git repository" at the end of its search; any other failure of the search is git.failed, and
    /// the file is not read.
    /// </summary>
    public static Result<Holding> HoldingOf(string estateRoot, string file, Runner? run = null, CancellationToken cancel = default)
    {
        var git = run ?? Command.Run;
        var (folder, name) = (Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileName(file));
        return Searched(git, estateRoot, cancel).Bind(estate => !estate ? Result.Ok(Holding.EstateInNoRepository) : Searched(git, folder, cancel).Bind(found => !found ? Result.Ok(Holding.InNoRepository)
            : Step(git, folder, ["ls-files", "--", (CaseBlind(git, folder, cancel) ? ":(literal,icase)" : ":(literal)") + name], cancel).Bind<Holding>(listed => listed.Length > 0 ? Holding.Tracked
                : Answered(git, folder, ["check-ignore", "--quiet", "--", name], cancel).Bind<Holding>(ignored => ignored.Code switch
                {
                    0 => Holding.Ignored,
                    1 => Holding.NotIgnored,
                    _ => Failed("check-ignore", ignored),
                }))));
    }

    /// <summary>
    /// A new branch holding one commit on HEAD, of HEAD's tree with the paths (from the repository's root) as the working tree
    /// has them, pushed to the origin. The caller's branch, index and working tree stay as they were; a branch that exists
    /// here or at the origin is refused before anything is written, and a push that fails, or is interrupted, leaves no branch behind.
    /// </summary>
    public static Result<Pushed> CommitAndPush(string estateRoot, IReadOnlyList<string> paths, string message, string branch, Runner? run = null, CancellationToken cancel = default)
    {
        var (git, state) = (run ?? Command.Run, new LocalState(estateRoot));
        return Root(git, estateRoot, cancel).Bind<Pushed>(root =>
        {
            var name = "refs/heads/" + branch;
            return Answered(git, root, ["check-ref-format", name], cancel).Bind<Pushed>(format => format.Code != 0
                ? new Error("git-branch.malformed", "'" + branch + "' is not a name git takes for a branch.", "Name the branch in words joined by '-' and '/', such as estate/evidence-dev.")
                : Answered(git, root, ["rev-parse", "--verify", "--quiet", name], cancel).Bind<Pushed>(here => here.Code == 0
                    ? Exists(branch, "in this repository")
                    : Answered(git, root, ["remote", "get-url", "origin"], cancel).Bind<Pushed>(remote => remote.Code switch
                    {
                        2 => new Error("git.no-origin", "The repository at " + root + " has no remote named origin, so " + branch + " cannot be pushed.", "Add the remote with git remote add origin <url>, then run estate again."),
                        not 0 => Failed("remote get-url", remote),
                        _ => Answered(git, root, ["ls-remote", "--exit-code", "origin", name], cancel).Bind<Pushed>(listed => listed.Code switch   // exit 2: the origin has no such branch
                        {
                            0 => Exists(branch, "at the origin"),
                            2 => Published(git, root, state, paths, message, name, branch, cancel),
                            _ => Unreachable("ls-remote", listed.Errors),
                        }),
                    })));
        });
    }

    /// <summary>The commit made and the branch updated, then pushed; whatever ends the push short of success, the local branch is deleted, with no token and a timeout of its own.</summary>
    private static Result<Pushed> Published(Runner git, string root, LocalState state, IReadOnlyList<string> paths, string message, string name, string branch, CancellationToken cancel) =>
        Committed(git, root, state, paths, message, cancel).Bind(commit => Step(git, root, ["update-ref", name, commit, ""], cancel).Bind<Pushed>(_ =>
        {
            var kept = false;
            try
            {
                var answer = Answered(git, root, ["push", "--porcelain", "--force-with-lease=" + name + ":", "origin", name + ":" + name], cancel)
                    .Bind<Pushed>(push => push.Code == 0 ? new Pushed(branch, commit) : Refusal(push, branch));
                kept = answer is Result<Pushed>.Ok;
                return answer;
            }
            finally
            {
                if (!kept)
                {
                    git(Built(root, ["update-ref", "-d", name, commit]) with { Timeout = Cleanup, Interruptible = false }, CancellationToken.None);
                }
            }
        }));

    /// <summary>Why the origin refused a push: a branch that appeared there meanwhile (stale info), a hook or policy (remote rejected), a credential it refused, or no answer.</summary>
    private static Error Refusal(Ran.Exited push, string branch)
    {
        var rejected = Rejected.Match(push.Output);
        return rejected.Success && rejected.Groups["reason"].Value.Contains("stale info", StringComparison.Ordinal) ? Exists(branch, "at the origin: it appeared there while estate pushed")
            : rejected.Success && rejected.Groups["how"].Value == "remote rejected" ? new Error("origin.rejected", "The origin refused the branch " + branch + ": " + rejected.Groups["reason"].Value + "; nothing was committed.",
                "Ask the repository's administrators which branch names its policy accepts, then name one.")
            : push.Errors.Contains("Authentication failed", StringComparison.Ordinal) || push.Errors.Contains("403", StringComparison.Ordinal) || push.Errors.Contains("Permission denied", StringComparison.Ordinal)
                ? new Error("origin.denied", "The origin refused this identity's credential: " + push.Errors.Trim() + "; nothing was committed.",
                    "Sign in through git's credential helper by running git fetch once in the repository, then run estate again.")
            : Unreachable("push", push.Errors);
    }

    /// <summary>The commit of HEAD's tree with the paths added, built in an index of its own under .estate/tmp/, so the caller's is never touched; the index is deleted however the commit ends.</summary>
    private static Result<string> Committed(Runner git, string root, LocalState state, IReadOnlyList<string> paths, string message, CancellationToken cancel) => state.Made(state.Temporary).Bind(temporary =>
    {
        var index = Path.Combine(temporary, "index-" + Path.GetRandomFileName());
        try
        {
            return Resolve(git, root, "HEAD", cancel).Bind(head => Step(git, root, ["read-tree", head], cancel, index).Bind(_ => Step(git, root, ["add", "--", .. paths], cancel, index))
                .Bind(_ => Step(git, root, ["write-tree"], cancel, index)).Bind(tree => Step(git, root, ["commit-tree", tree, "-p", head, "-m", message], cancel)));
        }
        finally
        {
            File.Delete(index);
        }
    });

    /// <summary>Whether a worktree stands at its commit with nothing changed or added, so a build of it reads the commit's files alone.</summary>
    private static bool Current(Runner git, string path, string commit, CancellationToken cancel) =>
        File.Exists(Path.Combine(path, ".git"))
        && Step(git, path, ["rev-parse", "HEAD"], cancel) is Result<string>.Ok { Value: var head } && head == commit
        && Step(git, path, ["status", "--porcelain", "--untracked-files=all"], cancel) is Result<string>.Ok { Value: "" };

    /// <summary>
    /// The sweep itself, under the worktrees lock: each worktree whose holders' lock can be taken exclusively (no running estate holds it
    /// shared) is removed with that lock file, as is a lock file left by a worktree that is gone; then git's records are pruned. The
    /// commits whose worktrees went, in ordinal order.
    /// </summary>
    private static Result<IReadOnlyList<string>> Swept(Runner git, string root, LocalState state, CancellationToken cancel)
    {
        var folder = new DirectoryInfo(state.Worktrees);
        var removed = new List<string>();
        foreach (var commit in folder.GetDirectories().Select(d => d.Name).Concat(folder.GetFiles("*.lock").Select(f => Path.GetFileNameWithoutExtension(f.Name))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (FileLock.Take(state.WorktreeHolders(commit), TimeSpan.Zero, cancel) is not Result<FileLock>.Ok { Value: var free })
            {
                continue;   // held by a running estate, this one included
            }

            var gone = !Directory.Exists(state.Worktree(commit));
            using (free)
            {
                if (!gone && Removed(git, root, state.Worktree(commit), cancel))
                {
                    removed.Add(commit);
                    gone = true;
                }
            }

            if (gone)
            {
                File.Delete(state.WorktreeHolders(commit));
            }
        }

        return Step(git, root, ["worktree", "prune"], cancel).Map<IReadOnlyList<string>>(_ => removed);
    }

    /// <summary>Removes a worktree and git's record of it; false when git cannot, such as while something holds a file open.</summary>
    private static bool Removed(Runner git, string root, string path, CancellationToken cancel) => Answered(git, root, ["worktree", "remove", "--force", "--force", path], cancel) is Result<Ran.Exited>.Ok { Value.Code: 0 };

    /// <summary>Whether git's search upward from the folder finds a repository: false only for git's own "not a git repository"; any other failure is its error.</summary>
    private static Result<bool> Searched(Runner git, string folder, CancellationToken cancel) =>
        Root(git, folder, cancel).Match<Result<bool>>(_ => true, error => error.Code == "git.not-a-repository" ? Result.Ok(false) : error);

    /// <summary>Whether the file system opens a file in the folder whatever the case of its name: on Windows and macOS, and wherever git's core.ignorecase is true.</summary>
    private static bool CaseBlind(Runner git, string folder, CancellationToken cancel) => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        || Answered(git, folder, ["config", "--bool", "core.ignorecase"], cancel) is Result<Ran.Exited>.Ok { Value: { Code: 0 } value } && value.Output.Trim() == "true";

    /// <summary>Whether the repository holds only part of its history (a clone with --depth), where merge-base cannot see a fork past the boundary.</summary>
    private static bool Shallow(Runner git, string root, CancellationToken cancel) => Step(git, root, ["rev-parse", "--is-shallow-repository"], cancel) is Result<string>.Ok { Value: "true" };

    /// <summary>
    /// The top level of the repository the directory is in, from git's own answer: git's "not a git repository (or any of the parent
    /// directories)" at the end of its search, or a directory it cannot change to, is git.not-a-repository; a repository git refuses
    /// because another user owns it (git 2.35.2's safe.directory) is git.dubious-ownership, naming the path git names; anything else,
    /// such as a .git file whose gitdir names no repository ("not a git repository: nowhere"), is git.failed quoting git.
    /// </summary>
    private static Result<string> Root(Runner git, string directory, CancellationToken cancel) => Answered(git, directory, ["rev-parse", "--show-toplevel"], cancel).Bind<string>(top =>
        top.Code == 0 ? Path.GetFullPath(top.Output.Trim())
        : top.Errors.Contains("not a git repository (or any ", StringComparison.Ordinal) || top.Errors.Contains("cannot change to", StringComparison.Ordinal)
            ? new Error("git.not-a-repository", directory + " is not in a git repository: " + top.Errors.Trim(), "Run estate in a clone of the repository, or name the clone's folder.")
        : top.Errors.Contains("detected dubious ownership", StringComparison.Ordinal) && (Refused.Match(top.Errors) is var named && named.Success ? named.Groups["path"].Value : directory) is var refused
            ? new Error("git.dubious-ownership", "git refuses " + refused + ": the repository belongs to another user, and git's safe.directory setting does not name it.",
                "Run git config --global --add safe.directory " + refused + " when you trust the repository, then run estate again.")
        : Failed("rev-parse", top));

    /// <summary>The commit a ref names: exit 1 is the one answer rev-parse --verify --quiet gives for a ref that names none; any other failure is git's, such as an old git refusing --end-of-options.</summary>
    private static Result<string> Resolve(Runner git, string root, string reference, CancellationToken cancel) => Answered(git, root, ["rev-parse", "--verify", "--quiet", "--end-of-options", reference + "^{commit}"], cancel).Bind<string>(resolved => resolved.Code switch
    {
        0 => resolved.Output.Trim(),
        1 => new Error("ref.unresolved", "'" + reference + "' names no commit in " + root + ".", "Name a branch, tag or commit the repository holds; git fetch brings the origin's."),
        _ => Failed("rev-parse", resolved),
    });

    private static Error Exists(string branch, string where) => new Error("git-branch.exists", "The branch " + branch + " already exists " + where + ".", "Name a new branch, or delete " + branch + " where it exists once its review is done.");

    private static Error Unreachable(string command, string errors) => new Error(
        "origin.unreachable", "git " + command + " to the origin failed, and nothing was committed: " + errors.Trim(),
        "Check that git fetch reaches the origin, signing in through git's own credential helper when it asks, then run estate again.");

    /// <summary>A git command that failed: its error quoted, or its exit code named when it wrote nothing (a git killed by a signal, or one that ended by itself).</summary>
    private static Error Failed(string command, Ran.Exited git) => git.Errors.Trim() is { Length: > 0 } errors
        ? new Error("git.failed", "git " + command + " failed: " + errors, "Fix what git names, then run estate again.")
        : new Error("git.failed", "git " + command + " exited " + git.Code + " and wrote no error.", "Run git " + command + " by hand to see why it exits " + git.Code + ", then run estate again.");

    /// <summary>git that must succeed: its output less the final line break; a failure quotes git's error.</summary>
    private static Result<string> Step(Runner git, string directory, IReadOnlyList<string> arguments, CancellationToken cancel, string? index = null) =>
        Answered(git, directory, arguments, cancel, index).Bind<string>(step => step.Code == 0 ? step.Output.TrimEnd('\n') : Failed(arguments[0], step));

    /// <summary>
    /// git -C the directory, run: its exit, output and errors less any credential in a URL, or the error when no git runs (git.missing) or
    /// it ran past its timeout (git.timed-out; origin.unreachable for ls-remote and push, which wait on the origin).
    /// </summary>
    private static Result<Ran.Exited> Answered(Runner git, string directory, IReadOnlyList<string> arguments, CancellationToken cancel, string? index = null) => git(Built(directory, arguments, index), cancel) switch
    {
        Ran.Exited exited => exited with { Errors = Credential.Replace(exited.Errors, "") },
        Ran.NotFound notFound => new Error("git.missing", "git does not run here: " + notFound.Why, "Install git and put it on the PATH, then run estate doctor."),
        Ran.TimedOut when arguments[0] is "ls-remote" or "push" => new Error("origin.unreachable",
            "git " + arguments[0] + " to the origin did not answer in " + Command.Written(Timeout) + ", and nothing was committed.",
            "Check that git fetch reaches the origin, signing in through git's own credential helper when it asks, then run estate again."),
        Ran.TimedOut => new Error("git.timed-out", "git " + arguments[0] + " in " + directory + " ran for " + Command.Written(Timeout) + " without finishing, and estate stopped it.",
            "Run git " + arguments[0] + " in " + directory + " by hand to see what it waits for, such as a credential prompt or a lock another program holds."),
        _ => throw new UnreachableException(),
    };

    /// <summary>The git command: -C the directory, core.longpaths on, the environment of <see cref="Scrubbed"/> and every GIT_TRACE variable removed, ESTATE_SQL withheld, English messages, no terminal prompt, LFS smudge off, the search across file systems, and the index when one is given.</summary>
    private static Command Built(string directory, IReadOnlyList<string> arguments, string? index = null)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var variable in Scrubbed.Concat(Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(key => key.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase))))
        {
            environment[variable] = null;
        }

        environment["ESTATE_SQL"] = null;
        (environment["GIT_TERMINAL_PROMPT"], environment["GIT_LFS_SKIP_SMUDGE"]) = ("0", "1");
        (environment["GIT_DISCOVERY_ACROSS_FILESYSTEM"], environment["LC_ALL"]) = ("1", "C");
        if (index is not null)
        {
            environment["GIT_INDEX_FILE"] = index;
        }

        return new Command("git", ["-c", "core.longpaths=true", "-C", directory, .. arguments], Timeout) { Environment = environment };
    }
}
