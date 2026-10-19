using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChange.Budgets.Tests;
using Xunit;

namespace DbChange.Tests;

/// <summary>
/// The golden project (tests/Golden/project/) and the classic-minimal project, each copied beside the corpus's two stop files, which keep
/// dbchange's build settings out, and the sample changes: tests/Golden/changes/&lt;name&gt;/edits.txt, the edits that make the change,
/// and change.txt, where a test states it, the lines dbchange diff prints for it. An edits.txt names a file with <c>=== path</c>, then gives
/// each edit as the lines it replaces, each after <c>--- </c>, and the lines that replace them, each after <c>+++ </c>. An edit's text must
/// occur exactly once in its file, so a golden file that drifts fails the copy that edits it, naming the file.
/// </summary>
internal static partial class GoldenProject
{
    /// <summary>The golden project's publish profile, the pipeline's.</summary>
    public static string Profile { get; } = Path.Combine(Repository.Root, "tests", "Golden", "project", "profiles", "pipeline.publish.xml");

    private static readonly string Golden = Path.Combine(Repository.Root, "tests", "Golden");

    /// <summary>The sample changes the registry holds, in ordinal order of their names.</summary>
    public static IReadOnlyList<SampleChange> SampleChanges { get; } = [.. Directory.GetDirectories(Path.Combine(Golden, "changes")).Order(StringComparer.Ordinal).Select(Read)];

    /// <summary>The sample change of that name.</summary>
    public static SampleChange Change(string name) => SampleChanges.SingleOrDefault(c => c.Name == name) ?? throw new ArgumentException("tests/Golden/changes/ holds no " + name, nameof(name));

    /// <summary>The golden project under <paramref name="folder"/>/project/, the stop files beside it and each edit applied in turn; the .sqlproj.</summary>
    public static string CopyTo(string folder, params IEnumerable<GoldenEdit> edits)
    {
        Copied("project", folder);
        foreach (var edit in edits)
        {
            edit.ApplyTo(Path.Combine(folder, "project"));
        }

        return Path.Combine(folder, "project", "SampleCatalog.sqlproj");
    }

    /// <summary>The classic-minimal project under <paramref name="folder"/>/classic-minimal/, the stop files beside it; the .sqlproj.</summary>
    public static string ClassicMinimalTo(string folder)
    {
        Copied("classic-minimal", folder);
        return Path.Combine(folder, "classic-minimal", "ClassicMinimal.sqlproj");
    }

    /// <summary>tests/Golden/&lt;tree&gt;/ under the folder, bin/ and obj/ left out, and the two stop files at the folder's root.</summary>
    private static void Copied(string tree, string folder)
    {
        var files = Directory.EnumerateFiles(Path.Combine(Golden, tree), "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(Golden, f).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Concat([Path.Combine(Golden, "Directory.Build.props"), Path.Combine(Golden, "Directory.Packages.props")]);
        foreach (var file in files)
        {
            var to = Path.Combine(folder, Path.GetRelativePath(Golden, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    private static SampleChange Read(string folder)
    {
        var (edits, file, from, to) = (new List<GoldenEdit>(), "", new List<string>(), new List<string>());
        void Made()
        {
            if (from.Count > 0)
            {
                edits.Add(new GoldenEdit(file, string.Join('\n', from), string.Join('\n', to)));
            }

            (from, to) = ([], []);
        }

        foreach (var line in File.ReadAllLines(Path.Combine(folder, "edits.txt")))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal) || (line.StartsWith("---", StringComparison.Ordinal) && to.Count > 0))
            {
                Made();
            }

            file = line.StartsWith("=== ", StringComparison.Ordinal) ? line[4..] : file;
            (line.StartsWith("---", StringComparison.Ordinal) ? from : line.StartsWith("+++", StringComparison.Ordinal) ? to : null)?.Add(line.Length > 4 ? line[4..] : "");
        }

        Made();
        var change = Path.Combine(folder, "change.txt");
        return new SampleChange(Path.GetFileName(folder), edits, File.Exists(change) ? [.. File.ReadAllLines(change).Where(l => l.Length > 0)] : []);
    }
}

/// <summary>A sample change of the golden project: its name, the edits that make it, and the lines dbchange diff prints for it, none where no test states them.</summary>
internal sealed record SampleChange(string Name, IReadOnlyList<GoldenEdit> Edits, IReadOnlyList<string> Lines)
{
    public override string ToString() => Name;
}

/// <summary>One find-and-replace in a file of a copied project; the text must occur in the file exactly once.</summary>
internal sealed record GoldenEdit(string File, string From, string To)
{
    public void ApplyTo(string project)
    {
        var path = Path.Combine(project, File);
        var text = System.IO.File.ReadAllText(path);
        Assert.True(text.Split(From).Length == 2, path + " holds '" + From + "' " + (text.Split(From).Length - 1) + " times, where an edit needs it once");
        System.IO.File.WriteAllText(path, text.Replace(From, To, StringComparison.Ordinal));
    }
}
