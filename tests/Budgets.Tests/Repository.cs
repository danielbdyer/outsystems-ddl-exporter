using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DbChange.Io;

namespace DbChange.Budgets.Tests;

/// <summary>
/// The repository: its root (the nearest directory above the test assembly holding DbChange.sln), and its files as git
/// sees them, tracked or untracked but not ignored, so bin/, obj/, .dbchange/ and the agents' worktrees never count.
/// </summary>
internal static class Repository
{
    public static string Root { get; } = Locate();

    /// <summary>Every file, relative to the root with '/' separators, in ordinal order.</summary>
    public static IReadOnlyList<string> Files => FileList.Value;

    /// <summary>The v3 MSBuild files: every project outside archive/, and the Directory.* files at the root.</summary>
    public static IEnumerable<string> MsBuildFiles => Files.Where(f => !f.StartsWith("archive/", StringComparison.Ordinal)
        && (f.EndsWith(".csproj", StringComparison.Ordinal) || (!f.Contains('/', StringComparison.Ordinal) && f.StartsWith("Directory.", StringComparison.Ordinal))));

    private static readonly Lazy<IReadOnlyList<string>> FileList = new(ListFiles);

    private static readonly Lazy<HashSet<string>> EntrySet = new(() =>
        Files.SelectMany(f => f.Split('/').Select((_, i) => string.Join('/', f.Split('/').Take(i + 1)))).ToHashSet(StringComparer.Ordinal));

    /// <summary>Every file and every directory, relative to the root, with no trailing '/'.</summary>
    public static IReadOnlyCollection<string> Entries => EntrySet.Value;

    /// <summary>Whether a relative path names a file or a directory in the repository; a trailing '/' is optional.</summary>
    public static bool Contains(string path) => EntrySet.Value.Contains(path.TrimEnd('/'));

    public static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    public static string[] Lines(string path) => File.ReadAllLines(Path.Combine(Root, path));

    public static XDocument Xml(string path) => XDocument.Load(Path.Combine(Root, path));

    /// <summary>Physical lines as <c>wc -l</c> counts them, plus a last line that has no newline.</summary>
    public static int PhysicalLines(string path)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, path));
        return bytes.Count(b => b == (byte)'\n') + (bytes.Length > 0 && bytes[^1] != (byte)'\n' ? 1 : 0);
    }

    /// <summary>A glob as a regular expression over a relative path: ** any run of segments, * any run within one, ? one character.</summary>
    public static Regex Glob(string glob) => new("^" + Regex.Replace(Regex.Escape(glob), @"\\\*\\\*/|\\\*\\\*|\\\*|\\\?", m => m.Value switch
    {
        @"\*\*/" => "(?:.*/)?",
        @"\*\*" => ".*",
        @"\*" => "[^/]*",
        _ => "[^/]",
    }) + "$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Every path an MSBuild file names in an Include, Update or Project attribute, or in an element's text, resolved
    /// against the file's directory and made relative to the root; a value holding another property is skipped.
    /// </summary>
    public static IEnumerable<string> PathsNamedBy(string msbuildFile)
    {
        var directory = Path.GetDirectoryName(Path.Combine(Root, msbuildFile))!;
        var values = Xml(msbuildFile).Descendants()
            .SelectMany(e => e.Attributes().Where(a => a.Name.LocalName is "Include" or "Update" or "Project").Select(a => a.Value)
                .Concat(e.HasElements ? [] : [e.Value]))
            .Select(v => v.Replace("$(MSBuildThisFileDirectory)", "", StringComparison.Ordinal).Replace("$(MSBuildProjectDirectory)", "", StringComparison.Ordinal))
            .SelectMany(v => v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => !v.Contains("$(", StringComparison.Ordinal) && (v.Contains('/', StringComparison.Ordinal) || v.Contains('\\', StringComparison.Ordinal) || v.Contains('.', StringComparison.Ordinal)));
        return values.Select(v => Path.GetRelativePath(Root, Path.GetFullPath(Path.Combine(directory, v))).Replace('\\', '/'));
    }

    private static IReadOnlyList<string> ListFiles()
    {
        var output = new Command("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], TimeSpan.FromMinutes(10)) { Directory = Root }.Run() switch
        {
            Ran.Exited { Code: 0 } listed => listed.Output,
            var git => throw new InvalidOperationException("git ls-files failed in " + Root + ": " + git),
        };
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(f => File.Exists(Path.Combine(Root, f)))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DbChange.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("DbChange.sln not found above " + AppContext.BaseDirectory);
    }
}
