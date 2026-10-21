using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

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
        foreach (var (key, value) in ((string, string)[])[("user.name", "DbChange Test"), ("user.email", "dbchange-test@example.invalid"),
            ("commit.gpgsign", "false"), ("core.autocrlf", "false")])
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

    /// <summary>A clone beside this repository, made with the options given before the URL, in a folder of its own, so the tests of
    /// one class fixture each clone it once; its root.</summary>
    public string Clone(params string[] arguments)
    {
        var clone = Enumerable.Range(1, int.MaxValue - 1).Select(n => Path.Combine(Parent, n == 1 ? "clone" : "clone-" + n))
            .First(path => !Directory.Exists(path));
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
    public List<string> Worktrees() =>
        Git("worktree", "list", "--porcelain").Split('\n').Where(l => l.StartsWith("worktree ", StringComparison.Ordinal)).ToList();

    public void Dispose()
    {
        Io.Git.Release(Root);
        folder.Dispose();
    }

    private string Run(string directory, params string[] arguments) => Programs.TestGit(directory, Parent, arguments);
}
