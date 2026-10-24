using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DbChangeLite;

public abstract record Source
{
    public sealed record Ref(string Name) : Source;

    public sealed record Dacpac(string Path) : Source;

    public sealed record Project(string Path) : Source;

    public static Source Parse(string text, string flag)
    {
        var at = text.IndexOf(':', StringComparison.Ordinal);
        var (kind, value) = at > 0 ? (text[..at], text[(at + 1)..]) : ("", "");
        return kind switch
        {
            "ref" when value.Length > 0 => new Ref(value),
            "dacpac" when value.Length > 0 => new Dacpac(value),
            "project" when value.Length > 0 => new Project(value),
            _ => throw Stop.Of("bad-arguments", "source.form", flag, flag + " is " + text + ", and a source is ref:<git ref>, dacpac:<path> or project:<path>.",
                "Give " + flag + " as ref:<git ref>, dacpac:<path> or project:<path>."),
        };
    }

    public override string ToString() => this switch
    {
        Ref r => "ref:" + r.Name,
        Dacpac d => "dacpac:" + d.Path,
        Project p => "project:" + p.Path,
        _ => "",
    };
}

public sealed record Built(string PackagePath, string Fingerprint, string? Commit, string? OntoCommit, string? Tree, string Describe);

public static partial class Ssdt
{
    public const string Targets = "Microsoft.Data.Tools.Schema.SqlTasks.targets";

    public static Built Package(Source source, Source.Ref? onto, Run run)
    {
        switch (source)
        {
            case Source.Dacpac dacpac:
                var path = Path.GetFullPath(Path.Combine(run.Root, dacpac.Path));
                if (!File.Exists(path))
                {
                    throw Stop.Of("bad-arguments", "source.dacpac-missing", dacpac.Path, "There is no package at " + dacpac.Path + ".", "Give dacpac:<path> an existing .dacpac.");
                }

                return new Built(path, DbChangeLite.Fingerprint.OfBytes(File.ReadAllBytes(path)), null, null, null, source.ToString());
            case Source.Project project:
                var folder = Path.GetFullPath(Path.Combine(run.Root, project.Path));
                var sqlproj = folder.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase) ? folder : OneProject(folder);
                var fingerprint = FolderFingerprint(Path.GetDirectoryName(sqlproj)!);
                return new Built(Cache.Package(run, fingerprint, () => Build(sqlproj, run)), fingerprint, null, null, null, source.ToString());
            case Source.Ref reference:
                return Checkout(reference, onto, run);
            default:
                throw new InvalidOperationException(source.ToString());
        }
    }

    private static Built Checkout(Source.Ref reference, Source.Ref? onto, Run run)
    {
        var commit = Git(run.Root, ["rev-parse", "--verify", reference.Name + "^{commit}"], run.Cancel, "source.ref-unknown", reference.Name).Trim();
        var ontoCommit = onto is null ? null : Git(run.Root, ["rev-parse", "--verify", onto.Name + "^{commit}"], run.Cancel, "source.ref-unknown", onto.Name).Trim();
        var worktree = Path.Combine(run.Folder, "src-" + commit[..12]);
        Git(run.Root, ["worktree", "add", "--detach", worktree, commit], run.Cancel, "source.worktree", reference.Name);
        try
        {
            if (ontoCommit is not null && ontoCommit != commit)
            {
                Merge(worktree, onto!.Name, ontoCommit, run);
            }

            var tree = Git(worktree, ["rev-parse", "HEAD^{tree}"], run.Cancel, "source.tree", reference.Name).Trim();
            var describe = reference + (onto is null ? "" : " onto " + onto);
            return new Built(Cache.Package(run, "tree-" + tree, () => Build(OneProject(worktree), run)), tree, commit, ontoCommit, tree, describe);
        }
        finally
        {
            Exec("git", ["-C", run.Root, "worktree", "remove", "--force", worktree], run.Root, TimeSpan.FromMinutes(2), CancellationToken.None);
        }
    }

    private static void Merge(string worktree, string ontoName, string ontoCommit, Run run)
    {
        var merge = Exec("git", ["-C", worktree, "-c", "user.name=dbchange-lite", "-c", "user.email=dbchange-lite@localhost", "merge", "--no-ff", "--no-edit", ontoCommit],
            worktree, TimeSpan.FromMinutes(5), run.Cancel);
        if (merge.Code == 0)
        {
            return;
        }

        var conflicts = Exec("git", ["-C", worktree, "diff", "--name-only", "--diff-filter=U"], worktree, TimeSpan.FromMinutes(1), run.Cancel).Output.Trim();
        Exec("git", ["-C", worktree, "merge", "--abort"], worktree, TimeSpan.FromMinutes(1), CancellationToken.None);
        throw Stop.Of("needs-input", "merge.conflict", ontoName, "Merging " + ontoName + " into the change conflicts in " + conflicts.ReplaceLineEndings(", ") + ".",
            "Merge " + ontoName + " into the branch, resolve each file, commit, and run the verb again.");
    }

    public static string OneProject(string folder)
    {
        var named = Environment.GetEnvironmentVariable("DBCHANGE_LITE_PROJECT");
        if (named is { Length: > 0 } && File.Exists(Path.Combine(folder, named)))
        {
            return Path.Combine(folder, named);
        }

        var projects = Directory.EnumerateFiles(folder, "*.sqlproj", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj" or ".dbchange")).Order(StringComparer.Ordinal).ToList();
        return projects switch
        {
            [var one] => one,
            [] => throw Stop.Of("bad-arguments", "project.missing", folder, folder + " holds no .sqlproj.", "Name a source that holds one SSDT project."),
            _ => throw Stop.Of("bad-arguments", "project.ambiguous", folder, folder + " holds several projects: " + string.Join(", ", projects.Select(p => Path.GetRelativePath(folder, p))) + ".",
                "Set DBCHANGE_LITE_PROJECT to the project's path from the repository root."),
        };
    }

    public static string Build(string project, Run run)
    {
        if (!File.Exists(Path.Combine(run.Tool, Targets)))
        {
            throw Stop.Of("configuration-refused", "tool.targets-missing", run.Tool, "The tool folder " + run.Tool + " holds no " + Targets + ", so no SSDT project can build.",
                "Run ci/publish.sh (ci/publish.ps1 on Windows) and run dbchange-lite from the published folder, or set DBCHANGE_LITE_TOOL to it.");
        }

        StopFiles(run.Folder);
        var output = Path.Combine(run.Folder, "build-" + Path.GetFileNameWithoutExtension(project) + "-" + Guid.NewGuid().ToString("N")[..8]) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(output);
        var build = Exec("dotnet",
        [
            "build", project, "-c", "Release", "--no-restore", "-nologo", "-tl:off", "-v:m", "-nodeReuse:false", "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true",
            "-p:NETCoreTargetsPath=" + run.Tool, "-p:SQLDBExtensionsRefPath=" + run.Tool, "-p:TargetFrameworkRootPath=" + Path.Combine(run.Tool, "refasm"),
            "-p:OutputPath=" + output, "-p:BaseIntermediateOutputPath=" + output + "obj" + Path.DirectorySeparatorChar, "-p:IntermediateOutputPath=" + output + "obj" + Path.DirectorySeparatorChar,
        ], Path.GetDirectoryName(project)!, TimeSpan.FromMinutes(10), run.Cancel);
        var package = Directory.EnumerateFiles(output, "*.dacpac", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => !Path.GetFileName(p).Equals("master.dacpac", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).Equals("msdb.dacpac", StringComparison.OrdinalIgnoreCase));
        if (build.Code != 0 || package is null)
        {
            var errors = (build.Output + "\n" + build.Error).Split('\n').Where(l => l.Contains(": error ", StringComparison.Ordinal)).Select(l => l.Trim()).Distinct().ToList();
            throw new Stop(Contract.Of("build-failed"), Finding.Error("build.failed", Path.GetFileName(project),
                "The build of " + Path.GetFileName(project) + " failed" + (errors.Count == 0 ? " with exit " + build.Code + "." : ": " + string.Join(" | ", errors.Take(10))),
                "Fix each error at the file and line named, then run the verb again."));
        }

        return package;
    }

}
