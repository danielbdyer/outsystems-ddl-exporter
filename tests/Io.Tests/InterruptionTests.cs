using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// Ctrl-C, SIGTERM and --timeout stop a verb with an interrupted answer at exit 130 (VALUES.md O7; cli/exits.frozen): a verb waiting on a
/// lock another dbchange process holds stops at that wait, leaves the lock to its holder and makes nothing.
/// </summary>
public sealed class InterruptionTests : IDisposable
{
    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "O7")]
    public void A_verb_waiting_on_a_held_lock_and_given_timeout_1_answers_interrupted_at_exit_130_within_3_seconds_and_changes_nothing()
    {
        var commit = scratch.Commit("first", ("a.sql", "SELECT 1;\n"));
        var state = new LocalState(scratch.Root);
        using var held = GitTests.Ok(FileLock.Take(state.WorktreesLock, TimeSpan.Zero));
        using var output = new MemoryStream();
        var clock = Stopwatch.StartNew();

        var exit = Cli.Program.Run(["read", "--from", "ref:HEAD", "--timeout", "1", "--json"], output, new Checkout(scratch.Root, scratch.Root, null));

        var answer = JsonNode.Parse(output.ToArray())!;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), "the interrupted verb answered after " + clock.Elapsed);
        Assert.Equal(130, exit);
        VerbAnswer.Valid("dbchange.read.1.schema.json", answer);
        Assert.Equal(("interrupted", 130), ((string?)answer["outcome"], (int)answer["exit"]!));
        Assert.Equal("dbchange read stopped after --timeout 1: it ended the programs it had started and released its locks.", (string?)answer["message"]);
        Assert.Empty(answer["findings"]!.AsArray());
        Assert.False(Directory.Exists(state.Worktree(commit)), "the interrupted verb made the worktree");
        GitTests.Failed(FileLock.Take(state.WorktreesLock, TimeSpan.Zero), "lock.timed-out");   // still this test's
    }

    /// <summary>
    /// A wait the timeout wakes reads the timeout as its cause. A token runs its callbacks newest first, so a wait registered after After,
    /// as FileLock's is, wakes before any callback After registered itself; the cause was once set by such a callback, and a verb the
    /// timeout stopped could answer "stopped after an interruption".
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "O7")]
    public void A_wait_the_timeout_wakes_reads_the_timeout_as_its_cause()
    {
        using var interruption = Interruption.Quiet();
        interruption.After(TimeSpan.FromSeconds(1));
        string? cause = null;
        using var woken = new ManualResetEventSlim();
        using var waiting = interruption.Token.Register(() =>
        {
            cause = interruption.Cause;
            woken.Set();
        });

        Assert.True(woken.Wait(TimeSpan.FromSeconds(30)), "the timeout did not wake the wait");
        Assert.Equal("--timeout 1", cause);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("--timeout", "0", "arguments.timeout")]
    [InlineData("--timeout", "86401", "arguments.timeout")]
    [InlineData("--timeout", "1.5", "arguments.timeout")]
    [InlineData("--timeout", "--json", "arguments.timeout")]
    [InlineData("--from", "ref:HEAD", "arguments.missing-flag")]
    public void A_timeout_that_is_no_whole_number_of_seconds_from_1_to_86400_is_arguments_timeout_at_exit_1(string flag, string value, string code)
    {
        using var output = new MemoryStream();

        var exit = Cli.Program.Run(["diff", flag, value, "--json"], output, new Checkout(scratch.Root, scratch.Root, null));

        var answer = JsonNode.Parse(output.ToArray())!;
        Assert.Equal((1, code), (exit, (string?)answer["findings"]![0]!["code"]));
        Assert.Equal(code == "arguments.timeout", ((string?)answer["findings"]![0]!["remedy"])!.Contains("--timeout 600", StringComparison.Ordinal));
    }
}

/// <summary>
/// The published dbchange stopped from outside while it waits on a held lock: on Windows by --timeout 2, the process-level answer this
/// operating system's test asserts, since no test sends the Ctrl-C key; on Linux and macOS by SIGINT and then, in a second run, SIGTERM.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class InterruptedProcessTests(PublishedTool tool) : IDisposable
{
    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "build")]
    [Trait("Value", "O7")]
    public void A_signal_or_a_timeout_stops_a_waiting_dbchange_with_exit_130_and_an_interrupted_answer()
    {
        scratch.Commit("first", ("a.sql", "SELECT 1;\n"));
        using var held = GitTests.Ok(FileLock.Take(new LocalState(scratch.Root).WorktreesLock, TimeSpan.Zero));

        (string? Signal, string Cause)[] ways = OperatingSystem.IsWindows() ? [(null, "--timeout 2")] : [("INT", "Ctrl-C"), ("TERM", "SIGTERM")];
        foreach (var (signal, cause) in ways)
        {
            var (exit, output) = signal is null ? Timed() : Signalled(signal);

            var answer = JsonNode.Parse(output)!;
            Assert.Equal(130, exit);
            Assert.Equal("interrupted", (string?)answer["outcome"]);
            Assert.Contains(cause, (string?)answer["message"], StringComparison.Ordinal);
        }
    }

    /// <summary>dbchange read at the repository, given --timeout 2, through io/Command.</summary>
    private (int Exit, string Output) Timed()
    {
        var ran = Assert.IsType<Ran.Exited>(new Command("dotnet", [Path.Combine(tool.Folder, "dbchange.dll"), "read", "--from", "ref:HEAD", "--timeout", "2", "--json"], TimeSpan.FromMinutes(1)) { Directory = scratch.Root }.Run());
        return (ran.Code, ran.Output);
    }

    /// <summary>dbchange read at the repository, sent the signal by kill once it has had three seconds to reach its lock wait; a raw process, since the test needs its id while it runs.</summary>
    private (int Exit, string Output) Signalled(string signal)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", [Path.Combine(tool.Folder, "dbchange.dll"), "read", "--from", "ref:HEAD", "--json"]) { WorkingDirectory = scratch.Root, RedirectStandardOutput = true })!;
        Thread.Sleep(TimeSpan.FromSeconds(3));
        Assert.IsType<Ran.Exited>(new Command("kill", ["-" + signal, process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(10)).Run());
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(20)), "dbchange did not stop within twenty seconds of SIG" + signal);
        return (process.ExitCode, output);
    }
}
