using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Estate.Kernel;

namespace Estate.Io;

/// <summary>
/// Every path under .estate/, the folder estate keeps beside an estate's checkout, named once (R7): worktrees and their holders' locks,
/// builds, runs, the copy registry and its lock, and the temporary folder, all under one root per run, the estate's root
/// (cli's Checkout.Root). <see cref="Made"/> creates a folder here and, with it, .estate/.gitignore holding "*", so the folder ignores
/// itself in a repository whose .gitignore does not name it (git status lists nothing under it, and check-ignore names the file); when
/// the folder is the worktrees', it also writes the stop files a ref's build needs: an empty Directory.Build.props, Directory.Build.targets
/// and Directory.Packages.props and an empty Directory.Build.rsp, since MSBuild searches upward from a project for each of them and would
/// otherwise reach the enclosing checkout's, so a ref builds with its own MSBuild settings alone. global.json gets no stop file: it chooses
/// the SDK, which is the machine's toolchain, and a ref that commits its own uses it. <see cref="UserSqlEnv"/> is the one file outside a
/// checkout, ~/.estate/sql.env, which ci/sql.sh and ci/sql.ps1 write; it is null where the user's profile folder is unknown (HOME unset in a
/// container or a service), and the local server treats it as absent.
/// </summary>
public sealed record LocalState(string Root)
{
    public const string Name = ".estate";

    /// <summary>The copy registry as messages name it, from the estate's root.</summary>
    public const string CopiesName = Name + "/copies.json";

    private static readonly (string File, string Text)[] StopFiles =
        [("Directory.Build.props", "<Project />\n"), ("Directory.Build.targets", "<Project />\n"), ("Directory.Packages.props", "<Project />\n"), ("Directory.Build.rsp", "")];

    public string Folder => Path.Combine(Root, Name);

    public string Worktrees => Path.Combine(Folder, "worktrees");

    public string WorktreesLock => Path.Combine(Folder, "worktrees.lock");

    public string Builds => Path.Combine(Folder, "build");

    public string Runs => Path.Combine(Folder, "runs");

    public string Copies => Path.Combine(Folder, "copies.json");

    public string CopiesLock => Path.Combine(Folder, "copies.lock");

    public string Temporary => Path.Combine(Folder, "tmp");

    public static string? UserSqlEnv => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } profile ? Path.Combine(profile, Name, "sql.env") : null;

    /// <summary>A ref's worktree, by its commit.</summary>
    public string Worktree(string commit) => Path.Combine(Worktrees, commit);

    /// <summary>The lock every estate process using a worktree holds shared, and a sweep must take exclusively before it removes the worktree.</summary>
    public string WorktreeHolders(string commit) => Path.Combine(Worktrees, commit + ".lock");

    /// <summary>A build's folder, by the commit or by the inputs' fingerprint.</summary>
    public string Build(string key) => Path.Combine(Builds, key);

    /// <summary>The folder made, with .estate/.gitignore and, for the worktrees, the stop files, each written only when absent; the folder's path.</summary>
    public Result<string> Made(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (Write.FileSystemFailure(e))
        {
            return Write.Unwritable(folder, e);
        }

        IEnumerable<(string Path, string Text)> files = [(Path.Combine(Folder, ".gitignore"), "*\n")];
        if (Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Worktrees), StringComparison.Ordinal))
        {
            files = files.Concat(StopFiles.Select(stop => (Path.Combine(Worktrees, stop.File), stop.Text)));
        }

        return Result.All(files.Where(f => !File.Exists(f.Path)).Select(f => Write.Text(f.Path, f.Text))).Map(_ => folder);
    }
}
