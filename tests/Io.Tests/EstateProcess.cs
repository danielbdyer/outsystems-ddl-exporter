using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// This test assembly run as a program (dotnet Estate.Io.Tests.dll): one estate process on a clone, as a second estate
/// invocation, another agent in the same clone or the gate proving another branch is. Each step waits for a line on its
/// standard input: it prints ready; takes the ref's worktree (Git.At) and prints it with the milliseconds At took; then,
/// when a project is named, builds it there and prints the package. An error is printed as its code and message, exit 1.
/// </summary>
internal static class EstateProcess
{
    /// <summary>Arguments: the repository and the ref; then, to build, the project from the repository's root, the tool folder and the build root.</summary>
    public static int Main(string[] arguments)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.WriteLine("ready");
        Console.ReadLine();
        var clock = Stopwatch.StartNew();
        var taken = Git.At(arguments[0], arguments[1]);
        Console.WriteLine(taken.Match(at => at.Path + "\n" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture), error => Printed(error) + "\n-1"));
        Console.ReadLine();
        var built = taken.Bind(at => arguments.Length < 5 ? Result.Ok("") : Ssdt.Build(at, arguments[2], arguments[3], arguments[4]).Map(dacpac => dacpac.Path));
        Console.WriteLine(built.Match(dacpac => dacpac, Printed));
        return built is Result<string>.Ok ? 0 : 1;
    }

    /// <summary>
    /// One estate process per ref, started together and, once all are ready, released in the refs' order a gap of
    /// milliseconds apart. Only when every one has taken its worktree, so every sweep of the round is over and every holder
    /// still runs, are they let on to build. For each ref: its worktree, the milliseconds At took, its package or "".
    /// </summary>
    public static List<(Git.Worktree At, long Took, string Dacpac)> AtOnce(string repository, IReadOnlyList<string> references, long gap, params string[] build)
    {
        var processes = references.Select(reference => Process.Start(new ProcessStartInfo("dotnet", [typeof(EstateProcess).Assembly.Location, repository, reference, .. build])
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
        })!).Select(process => (Process: process, Errors: process.StandardError.ReadToEndAsync())).ToList();
        try
        {
            foreach (var (process, errors) in processes)
            {
                if (process.StandardOutput.ReadLine() != "ready")
                {
                    Assert.Fail("an estate process did not start: " + errors.Result);
                }
            }

            var clock = Stopwatch.StartNew();
            foreach (var (process, index) in processes.Select((p, i) => (p.Process, i)))
            {
                SpinWait.SpinUntil(() => clock.ElapsedMilliseconds >= index * gap);
                process.StandardInput.WriteLine();
            }

            var taken = processes.Select(p => (Path: p.Process.StandardOutput.ReadLine(), Took: p.Process.StandardOutput.ReadLine())).ToList();
            foreach (var (process, _) in processes.Where((_, i) => taken[i].Took is not null))   // one that ended early awaits no line
            {
                process.StandardInput.WriteLine();
            }

            return processes.Zip(references, taken).Select(run =>
            {
                var (p, reference, (path, took)) = run;
                var built = p.Process.StandardOutput.ReadToEnd().Trim();
                Assert.True(p.Process.WaitForExit(TimeSpan.FromMinutes(3)), "the estate process at " + reference + " overran three minutes");
                Assert.True(p.Process.ExitCode == 0, "the estate process at " + reference + ", released " + gap + " ms after the one before, exited " + p.Process.ExitCode + ":\n" + path + "\n" + built + "\n" + p.Errors.Result);
                return (new Git.Worktree(path!, reference), long.Parse(took!, CultureInfo.InvariantCulture), built);
            }).ToList();
        }
        finally
        {
            foreach (var (process, _) in processes.Where(p => !p.Process.HasExited))
            {
                process.Kill(entireProcessTree: true);
            }

            processes.ForEach(p => p.Process.Dispose());
        }
    }

    private static string Printed(Error error) => error.Code + ": " + error.Message.ReplaceLineEndings(" ");
}
