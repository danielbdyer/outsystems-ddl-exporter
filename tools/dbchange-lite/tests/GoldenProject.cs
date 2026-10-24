using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DbChangeLite.Tests;

public sealed class GoldenProject
{
    private GoldenProject(string folder, string project)
    {
        Folder = folder;
        Project = project;
    }

    public string Folder { get; }

    public string Project { get; }

    public static IReadOnlyList<string> SampleChanges => Directory.EnumerateDirectories(Path.Combine(Repo.Golden, "changes")).Select(Path.GetFileName).Select(n => n!).Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> LiteChanges => Directory.Exists(Path.Combine(Repo.Golden, "lite"))
        ? Directory.EnumerateDirectories(Path.Combine(Repo.Golden, "lite")).Select(Path.GetFileName).Select(n => n!).Order(StringComparer.Ordinal).ToList() : [];

    public static string Case(string name, string file) => Directory.Exists(Path.Combine(Repo.Golden, "changes", name))
        ? Path.Combine(Repo.Golden, "changes", name, file) : Path.Combine(Repo.Golden, "lite", name, file);

    public static GoldenProject Copy(Scratch scratch, string name)
    {
        var root = scratch.Under(name);
        var project = Path.Combine(root, "project");
        CopyTree(Path.Combine(Repo.Golden, "project"), project);
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project />\n");
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), "<Project />\n");
        return new GoldenProject(root, project);
    }

    public GoldenProject Edit(string name)
    {
        var edits = Case(name, "edits.txt");
        string? file = null;
        var old = new List<string>();
        var replacement = new List<string>();
        void Apply()
        {
            if (file is null || old.Count == 0)
            {
                return;
            }

            var path = Path.Combine(Project, file.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);
            var ending = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var before = string.Join(ending, old);
            var after = string.Join(ending, replacement);
            var first = text.IndexOf(before, StringComparison.Ordinal);
            if (first < 0 || text.IndexOf(before, first + 1, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(name + ": the edit of " + file + " must occur exactly once: " + before);
            }

            File.WriteAllText(path, text[..first] + after + text[(first + before.Length)..]);
            old.Clear();
            replacement.Clear();
        }

        foreach (var line in File.ReadAllLines(edits))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                Apply();
                file = line[4..].Trim();
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal) || line == "---")
            {
                if (replacement.Count > 0)
                {
                    Apply();
                }

                old.Add(line.Length > 4 ? line[4..] : "");
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal) || line == "+++")
            {
                replacement.Add(line.Length > 4 ? line[4..] : "");
            }
        }

        Apply();
        return this;
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).Where(d => !Skipped(Path.GetRelativePath(from, d))))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).Where(f => !Skipped(Path.GetRelativePath(from, f))))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    private static bool Skipped(string relative) => relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj");
}

internal sealed record Expected(string Copy, string Outcome, IReadOnlyList<string> Blockers, bool ProveIt)
{
    public static IReadOnlyList<Expected> Of(string name) => File.ReadAllLines(GoldenProject.Case(name, "predict.txt"))
        .Where(l => l.StartsWith("empty: ", StringComparison.Ordinal) || l.StartsWith("populated: ", StringComparison.Ordinal) || l.StartsWith("violating: ", StringComparison.Ordinal))
        .Select(l =>
        {
            var copy = l[..l.IndexOf(':', StringComparison.Ordinal)];
            var rest = l[(copy.Length + 2)..];
            var outcome = rest.Split(';')[0].Trim();
            var blockers = outcome.StartsWith("blocks by ", StringComparison.Ordinal)
                ? System.Text.RegularExpressions.Regex.Split(outcome[10..], @",\s+(?=[A-Z][A-Za-z]+ on \[)").Select(b => b.Trim()).ToList() : new List<string>();
            return new Expected(copy, outcome.StartsWith("blocks", StringComparison.Ordinal) ? "blocks" : outcome, blockers, rest.Contains("prove it", StringComparison.Ordinal));
        })
        .ToList();
}
