using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// The other modes stand in for programs io runs: lock (a holder of a lock file until its input ends), flood (a program
/// that fills both pipes), sleep and spawn (a program that outlives its timeout, and one that starts a child first),
/// utf8 (UTF-8 on both streams), env and environment (what a child inherits), and telemetry (the opt-out before DacFx loads).
/// </summary>
internal static class EstateProcess
{
    /// <summary>Arguments: a mode and its argument; or the repository and the ref, then, to build, the project from the repository's root, the tool folder and the build root.</summary>
    public static int Main(string[] arguments)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        switch (arguments)
        {
            case ["lock", var path]:
                return FileLock.Take(path, TimeSpan.FromMinutes(1)).Match(held =>
                {
                    using (held)
                    {
                        Console.WriteLine("held");
                        Console.ReadLine();   // until the input ends or the process is killed
                    }

                    return 0;
                }, error => Printed(error).Length > 0 ? 1 : 1);
            case ["flood", var count]:
                Flood(int.Parse(count, CultureInfo.InvariantCulture));
                return 0;
            case ["sleep", var seconds]:
                Console.WriteLine(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                Thread.Sleep(TimeSpan.FromSeconds(int.Parse(seconds, CultureInfo.InvariantCulture)));
                return 0;
            case ["spawn"]:
                using (var child = Process.Start(new ProcessStartInfo("dotnet", [typeof(EstateProcess).Assembly.Location, "sleep", "60"]))!)
                {
                    Console.WriteLine(child.Id.ToString(CultureInfo.InvariantCulture));
                    Console.Out.Flush();
                    Thread.Sleep(TimeSpan.FromSeconds(60));
                }

                return 0;
            case ["utf8"]:
                Console.Write("Café-Ω\n");
                Console.Error.Write("Café-Ω\n");
                return 0;
            case ["env", var name]:
                Console.WriteLine(Environment.GetEnvironmentVariable(name) ?? "<unset>");
                return 0;
            case ["environment"]:
                foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
                {
                    Console.WriteLine(variable.Key + "=" + variable.Value);
                }

                return 0;
            case ["telemetry"]:
                _ = LocalState.UserSqlEnv;   // the first touch of an io type, and nothing has called Telemetry.OptOut
                Console.WriteLine((Environment.GetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT") ?? "<unset>") + " " + (Environment.GetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT") ?? "<unset>") + " "
                    + (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name!.StartsWith("Microsoft.SqlServer.Dac", StringComparison.Ordinal)) ? "dacfx-loaded" : "dacfx-not-loaded"));
                return 0;
            default:
                return Estate(arguments);
        }
    }

    /// <summary>
    /// One estate process per ref, started together and, once all are ready, released in the refs' order a gap of
    /// milliseconds apart. Only when every one has taken its worktree, so every sweep of the round is over and every holder
    /// still runs, are they let on to build. For each ref: its worktree, the milliseconds At took, its package or "".
    /// </summary>
    public static List<(Git.Worktree At, long Took, string Dacpac)> AtOnce(string repository, IReadOnlyList<string> references, long gap, params string[] build)
    {
        var processes = references.Select(reference => Start([repository, reference, .. build])).Select(process => (Process: process, Errors: process.StandardError.ReadToEndAsync())).ToList();
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

    /// <summary>
    /// This assembly started as a program with its input, output and errors redirected and its input kept open, for a test that
    /// drives it line by line or holds it until it kills it; io's Command closes a program's input by design, so this stays a raw Process.
    /// </summary>
    public static Process Start(params string[] arguments) => Process.Start(new ProcessStartInfo("dotnet", [typeof(EstateProcess).Assembly.Location, .. arguments])
    {
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
    })!;

    private static int Estate(string[] arguments)
    {
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

    /// <summary>The count of 'o' on standard output and of 'e' on standard error, the two written at once, so a reader that takes one stream at a time stalls.</summary>
    private static void Flood(int count)
    {
        var errors = new Thread(() =>
        {
            using var stream = Console.OpenStandardError();
            stream.Write(Enumerable.Repeat((byte)'e', count).ToArray());
        });
        errors.Start();
        using (var stream = Console.OpenStandardOutput())
        {
            stream.Write(Enumerable.Repeat((byte)'o', count).ToArray());
        }

        errors.Join();
    }

    private static string Printed(Error error)
    {
        Console.WriteLine(error.Code + ": " + error.Message.ReplaceLineEndings(" "));
        return error.Code;
    }
}
