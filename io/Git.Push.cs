using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using DbChange.Kernel;

namespace DbChange.Io;

public static partial class Git
{
    /// <summary>What a push published: the new branch and its one commit.</summary>
    public sealed record Pushed(string Branch, string Commit);

    /// <summary>How long the deletion of a half-pushed branch may take after a failed or interrupted push.</summary>
    private static readonly TimeSpan Cleanup = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A push --porcelain line for a ref the origin refused: ! then the refspec, then [rejected] or [remote rejected] and the reason in
    /// parentheses.
    /// </summary>
    private static readonly Regex Rejected = new(@"^!\t[^\t]*\t\[(?<how>rejected|remote rejected)\] \((?<reason>[^)]*)\)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>
    /// A new branch holding one commit on HEAD, of HEAD's tree with the paths (from the repository's root) as the working tree
    /// has them, pushed to the origin. The caller's branch, index and working tree stay as they were; a branch that exists
    /// here or at the origin is refused before anything is written, and a push that fails, or is interrupted, leaves no branch behind.
    /// </summary>
    public static Result<Pushed> CommitAndPush(string repositoryRoot, IReadOnlyList<string> paths, string message, string branch,
        Runner? run = null, CancellationToken cancel = default)
    {
        var (git, state) = (run ?? Command.Run, new LocalState(repositoryRoot));
        return Root(git, repositoryRoot, cancel).Bind<Pushed>(root =>
        {
            var name = "refs/heads/" + branch;
            return Answered(git, root, ["check-ref-format", name], cancel).Bind<Pushed>(format => format.Code != 0
                ? new Error("git-branch.malformed", "'" + branch + "' is not a name git takes for a branch.",
                    "Name the branch in words joined by '-' and '/', such as dbchange/evidence-dev.")
                : Answered(git, root, ["rev-parse", "--verify", "--quiet", name], cancel).Bind<Pushed>(here => here.Code == 0
                    ? Exists(branch, "in this repository")
                    : Answered(git, root, ["remote", "get-url", "origin"], cancel).Bind<Pushed>(remote => remote.Code switch
                    {
                        2 => new Error("git.no-origin",
                            "The repository at " + root + " has no remote named origin, so " + branch + " cannot be pushed.",
                            "Add the remote with git remote add origin <url>, then run the command again."),
                        not 0 => Failed("remote get-url", remote),
                        _ => Answered(git, root, ["ls-remote", "--exit-code", "origin", name], cancel)
                            .Bind<Pushed>(listed => listed.Code switch   // exit 2: the origin has no such branch
                        {
                            0 => Exists(branch, "at the origin"),
                            2 => Published(git, root, state, paths, message, name, branch, cancel),
                            _ => Unreachable("ls-remote", listed.Errors),
                        }),
                    })));
        });
    }

    /// <summary>
    /// The commit made and the branch updated, then pushed; whatever ends the push short of success, the local branch is deleted, with no
    /// token and a timeout of its own.
    /// </summary>
    private static Result<Pushed> Published(Runner git, string root, LocalState state, IReadOnlyList<string> paths, string message,
        string name, string branch, CancellationToken cancel) =>
        Committed(git, root, state, paths, message, cancel)
            .Bind(commit => Step(git, root, ["update-ref", name, commit, ""], cancel).Bind<Pushed>(_ =>
        {
            var kept = false;
            try
            {
                var answer = Answered(git, root,
                    ["push", "--porcelain", "--force-with-lease=" + name + ":", "origin", name + ":" + name], cancel)
                    .Bind<Pushed>(push => push.Code == 0 ? new Pushed(branch, commit) : Refusal(push, branch));
                kept = answer is Result<Pushed>.Ok;
                return answer;
            }
            finally
            {
                if (!kept)
                {
                    git(Built(root, ["update-ref", "-d", name, commit]) with { Timeout = Cleanup, Interruptible = false },
                        CancellationToken.None);
                }
            }
        }));

    /// <summary>
    /// Why the origin refused a push: a branch that appeared there meanwhile (stale info), a hook or policy (remote rejected), a credential
    /// it refused, or no answer.
    /// </summary>
    private static Error Refusal(Ran.Exited push, string branch)
    {
        var rejected = Rejected.Match(push.Output);
        return rejected.Success && rejected.Groups["reason"].Value.Contains("stale info", StringComparison.Ordinal)
            ? Exists(branch, "at the origin: it appeared there while dbchange pushed")
            : rejected.Success && rejected.Groups["how"].Value == "remote rejected"
                ? new Error("origin.rejected",
                    "The origin refused the branch " + branch + ": " + rejected.Groups["reason"].Value + "; nothing was committed.",
                "Ask the repository's administrators which branch names its policy accepts, then name one.")
            : push.Errors.Contains("Authentication failed", StringComparison.Ordinal)
                || push.Errors.Contains("403", StringComparison.Ordinal)
                || push.Errors.Contains("Permission denied", StringComparison.Ordinal)
                ? new Error("origin.denied",
                    "The origin refused this identity's credential: " + push.Errors.Trim() + "; nothing was committed.",
                    "Sign in through git's credential helper by running git fetch once in the repository, then run the command again.")
            : Unreachable("push", push.Errors);
    }

    /// <summary>
    /// The commit of HEAD's tree with the paths added, built in an index of its own under .dbchange/tmp/, so the caller's is never touched;
    /// the index is deleted however the commit ends.
    /// </summary>
    private static Result<string> Committed(Runner git, string root, LocalState state, IReadOnlyList<string> paths, string message,
        CancellationToken cancel) => state.Made(state.Temporary).Bind(temporary =>
    {
        var index = Path.Combine(temporary, "index-" + Path.GetRandomFileName());
        try
        {
            return Resolve(git, root, "HEAD", cancel).Bind(head => Step(git, root, ["read-tree", head], cancel, index)
                .Bind(_ => Step(git, root, ["add", "--", .. paths], cancel, index))
                .Bind(_ => Step(git, root, ["write-tree"], cancel, index))
                .Bind(tree => Step(git, root, ["commit-tree", tree, "-p", head, "-m", message], cancel)));
        }
        finally
        {
            Write.Discard(index);
        }
    });
}
