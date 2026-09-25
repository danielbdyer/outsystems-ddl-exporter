using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Estate.Io;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// LAWS.md is generated (V3_INSTRUCTION_ARCHITECTURE.md §6.10, VALUES.md L6; DECISIONS.md, 2026-09-25): ci/laws.sh, and ci/laws.ps1 where
/// Windows runs it, write one row per test carrying a [Trait("Law", …)], a [Trait("Value", …)] or a [Trait("Exit", …)], with the trait's
/// value, the test's name in English and its full name, in three tables, then the rows of VALUES.md and the exits of V3_MILESTONES.md no
/// test declares; the generated banner first and no timestamp. The committed file is exactly what either generator writes, run after
/// run, and what the generators read from the sources is what <see cref="TestTraits"/> reads.
/// </summary>
public sealed class Laws
{
    private static readonly Regex TableRow = new(@"^\| (.+?) \| (.+?) \| `(.+?)` \|$", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L6")]
    public void LAWS_md_is_what_the_generators_write_from_the_tests_and_regenerates_byte_identically()
    {
        var committed = File.ReadAllBytes(Path.Combine(Repository.Root, "LAWS.md"));
        var written = Generators().Select(g => (g.Name, First: g.Write(), Second: g.Write())).ToList();

        Assert.NotEmpty(written);
        Assert.All(written, w => Assert.True(w.First.SequenceEqual(w.Second), w.Name + " wrote LAWS.md differently on a second run"));
        Assert.All(written, w => Assert.True(w.First.SequenceEqual(committed), "LAWS.md is stale against " + w.Name + ": run it and commit LAWS.md\n" + Encoding.UTF8.GetString(w.First)));
        var text = Encoding.UTF8.GetString(committed);
        Assert.Matches(Manifest.Banner, text.Split('\n')[0]);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L6")]
    public void LAWS_md_lists_each_Law_Value_and_Exit_trait_the_tests_declare_and_names_each_row_and_exit_none_declares()
    {
        var sections = File.ReadAllText(Path.Combine(Repository.Root, "LAWS.md")).Split("\n## ");
        string[] kinds = ["Law", "Value", "Exit"];

        Assert.Equal(kinds.Length, sections.Length);
        var listed = sections.SelectMany((s, i) => Rows(s).Select(r => kinds[i] + "\t" + r)).Order(StringComparer.Ordinal);
        var declared = TestTraits.All.SelectMany(t => t.Traits.Where(tr => kinds.Contains(tr.Name)).Select(tr => tr.Name + "\t" + tr.Value + "\t" + t.English + "\t" + t.FullName)).Order(StringComparer.Ordinal);
        Assert.Equal(declared, listed);
        var declaredValues = TestTraits.All.SelectMany(t => t.Values("Value")).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Documents.Values.Where(v => !declaredValues.Contains(v)).Order(StringComparer.Ordinal), Named(sections[1], "Rows of `VALUES.md` no test declares: ").Order(StringComparer.Ordinal));
        Assert.Equal(Documents.EveryExit.Where(e => !Documents.DeclaredExits.Contains(e)).Order(StringComparer.Ordinal), Named(sections[2], "Exits no test declares, which a person or a CI job runs: ").Order(StringComparer.Ordinal));
    }

    /// <summary>A section's table rows as value, English name and full name, tab-separated.</summary>
    private static IEnumerable<string> Rows(string section) =>
        section.Split('\n').Select(l => TableRow.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value + "\t" + m.Groups[2].Value + "\t" + m.Groups[3].Value);

    /// <summary>What the section's line opening with <paramref name="prefix"/> names, comma-separated and ending in a full stop; nothing for none.</summary>
    private static IEnumerable<string> Named(string section, string prefix)
    {
        var named = section.Split('\n').Single(l => l.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].TrimEnd('.');
        return named == "none" ? [] : named.Split(", ");
    }

    /// <summary>Each generator this machine can run: ci/laws.ps1 through pwsh, and ci/laws.sh through bash (on Windows, Git's own bash, never another on the PATH).</summary>
    private static IEnumerable<(string Name, Func<byte[]> Write)> Generators()
    {
        if (Runs("pwsh", "-NoProfile", "-Command", "exit 0"))
        {
            yield return ("ci/laws.ps1", () => Written("pwsh", "-NoProfile", "-File", Path.Combine(Repository.Root, "ci", "laws.ps1")));
        }

        var bash = OperatingSystem.IsWindows() ? GitBash() : "bash";
        if (bash is not null && Runs(bash, "-c", "exit 0"))
        {
            yield return ("ci/laws.sh", () => Written(bash, Path.Combine(Repository.Root, "ci", "laws.sh").Replace('\\', '/')));
        }
    }

    /// <summary>The generator run with an output file of its own; the bytes it wrote.</summary>
    private static byte[] Written(string file, params string[] arguments)
    {
        var output = Path.Combine(Path.GetTempPath(), "estate-laws-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            var (exit, log) = Programs.InRepository(file, [.. arguments, output.Replace('\\', '/')]).Finish().Joined();
            Assert.True(exit == 0, file + " " + string.Join(' ', arguments) + " exited " + exit + ":\n" + log);
            return File.ReadAllBytes(output);
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>Git for Windows' bash, beside the git on the PATH: git --exec-path is &lt;git&gt;/mingw64/libexec/git-core, and bash is &lt;git&gt;/bin/bash.exe.</summary>
    private static string? GitBash() =>
        Programs.InRepository("git", "--exec-path").Run() is Ran.Exited { Code: 0 } exited
            && Path.GetFullPath(Path.Combine(exited.Output.Trim(), "..", "..", "..", "bin", "bash.exe")) is var bash && File.Exists(bash) ? bash : null;

    /// <summary>Whether the program runs here: one that is not installed (Ran.NotFound) does not.</summary>
    private static bool Runs(string file, params string[] arguments) => Programs.InRepository(file, arguments).Run() is Ran.Exited { Code: 0 };
}
