using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// LAWS.md is generated (V3_INSTRUCTION_ARCHITECTURE.md §6.10, VALUES.md L6): ci/laws.sh, and ci/laws.ps1 where Windows runs it,
/// write one row per test carrying a [Trait("Law", …)], with the law, the test's name in English and its full name, the generated
/// banner first and no timestamp, so the committed file is exactly what either generator writes, run after run.
/// </summary>
public sealed class Laws
{
    [Fact]
    [Trait("Category", "fast")]
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
        Assert.Contains("| 3′ the model is complete | The fingerprint of a model is independent of the order its elements are given in | `Estate.Kernel.Tests.ElementTests.The_fingerprint_of_a_model_is_independent_of_the_order_its_elements_are_given_in` |", text, StringComparison.Ordinal);
        Assert.Contains("| the kernel cannot do I/O | ", text, StringComparison.Ordinal);
        Assert.Contains("| dependencies point one way | ", text, StringComparison.Ordinal);
        Assert.Contains("| 2′ a published copy matches its package | ", text, StringComparison.Ordinal);
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
            var (exit, log) = Run(file, [.. arguments, output.Replace('\\', '/')]);
            Assert.True(exit == 0, file + " " + string.Join(' ', arguments) + " exited " + exit + ":\n" + log);
            return File.ReadAllBytes(output);
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>Git for Windows' bash, beside the git on the PATH: git --exec-path is &lt;git&gt;/mingw64/libexec/git-core, and bash is &lt;git&gt;/bin/bash.exe.</summary>
    private static string? GitBash()
    {
        var (exit, path) = Run("git", ["--exec-path"]);
        var bash = exit == 0 ? Path.GetFullPath(Path.Combine(path.Trim(), "..", "..", "..", "bin", "bash.exe")) : null;
        return bash is not null && File.Exists(bash) ? bash : null;
    }

    private static bool Runs(string file, params string[] arguments)
    {
        try
        {
            return Run(file, arguments).Exit == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static (int Exit, string Output) Run(string file, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Repository.Root };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.Result);
    }
}
