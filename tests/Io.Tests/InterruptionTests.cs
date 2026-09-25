using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using Estate.Cli;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// Ctrl-C, SIGTERM and --timeout stop a verb with an interrupted answer at exit 130 (VALUES.md O7; cli/exits.frozen): a verb waiting on a
/// lock another estate holds stops at that wait, leaves the lock to its holder and makes nothing.
/// </summary>
public sealed class InterruptionTests : IDisposable
{
    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "fast")]
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
        ScratchEstate.Valid("estate.read.1.schema.json", answer);
        Assert.Equal(("interrupted", 130), ((string?)answer["outcome"], (int)answer["exit"]!));
        Assert.Equal("estate read stopped after --timeout 1: it ended the programs it had started and released its locks.", (string?)answer["message"]);
        Assert.Empty(answer["findings"]!.AsArray());
        Assert.False(Directory.Exists(state.Worktree(commit)), "the interrupted verb made the worktree");
        Assert.Equal("lock.timed-out", GitTests.Failed(FileLock.Take(state.WorktreesLock, TimeSpan.Zero)).Code);   // still this test's
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
/// The published estate stopped from outside while it waits on a held lock: on Windows by --timeout 2, the process-level answer this
/// operating system's test asserts, since no test sends the Ctrl-C key; on Linux and macOS by SIGINT and then, in a second run, SIGTERM.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class InterruptedProcessTests(PublishedTool tool) : IDisposable
{
    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "fast")]
    public void A_signal_or_a_timeout_stops_a_waiting_estate_with_exit_130_and_an_interrupted_answer()
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

    /// <summary>estate read at the repository, given --timeout 2, through io/Command.</summary>
    private (int Exit, string Output) Timed()
    {
        var ran = Assert.IsType<Ran.Exited>(new Command("dotnet", [Path.Combine(tool.Folder, "estate.dll"), "read", "--from", "ref:HEAD", "--timeout", "2", "--json"], TimeSpan.FromMinutes(1)) { Directory = scratch.Root }.Run());
        return (ran.Code, ran.Output);
    }

    /// <summary>estate read at the repository, sent the signal by kill once it has had three seconds to reach its lock wait; a raw process, since the test needs its id while it runs.</summary>
    private (int Exit, string Output) Signalled(string signal)
    {
        using var estate = Process.Start(new ProcessStartInfo("dotnet", [Path.Combine(tool.Folder, "estate.dll"), "read", "--from", "ref:HEAD", "--json"]) { WorkingDirectory = scratch.Root, RedirectStandardOutput = true })!;
        Thread.Sleep(TimeSpan.FromSeconds(3));
        Assert.IsType<Ran.Exited>(new Command("kill", ["-" + signal, estate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(10)).Run());
        var output = estate.StandardOutput.ReadToEnd();
        Assert.True(estate.WaitForExit(TimeSpan.FromSeconds(20)), "estate did not stop within twenty seconds of SIG" + signal);
        return (estate.ExitCode, output);
    }
}
