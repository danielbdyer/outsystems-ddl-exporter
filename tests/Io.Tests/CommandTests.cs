using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Estate.Budgets.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/Command, the one way io runs a program (R6): a bare name is found on the PATH and never in the working directory, where a
/// checkout could plant one; both streams arrive whole, apart and as UTF-8; a program past its timeout, or a run interrupted, ends
/// with every process it started. The programs run are this assembly's own modes (EstateProcess), so no test depends on a shell.
/// </summary>
public sealed class CommandTests : IDisposable
{
    private static readonly string Assembly = typeof(EstateProcess).Assembly.Location;

    private readonly string scratch = Directory.CreateTempSubdirectory("estate-command-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    [Trait("Category", "fast")]
    public void A_program_on_no_folder_of_the_PATH_is_NotFound_and_its_reason_names_the_folders_searched_and_never_the_working_directory()
    {
        var name = "estate-no-such-program-" + Guid.NewGuid().ToString("N")[..8];
        File.WriteAllText(Path.Combine(scratch, name + (OperatingSystem.IsWindows() ? ".exe" : "")), "");
        var first = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).First(Path.IsPathFullyQualified);

        var ran = new Command(name, [], TimeSpan.FromSeconds(10)) { Directory = scratch }.Run();

        var notFound = Assert.IsType<Ran.NotFound>(ran);
        Assert.Equal(name, notFound.Program);
        Assert.Contains(first, notFound.Why, StringComparison.Ordinal);
        Assert.DoesNotContain(scratch, notFound.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fact 2 of the specification: a .NET process whose working directory holds git.exe runs that file when it starts git by name,
    /// unless NoDefaultCurrentDirectoryInExePath is set. Command resolves the name itself, so the planted copy of whoami (on Windows) or a
    /// script printing "planted" (elsewhere) never runs, and git --version is the PATH's git.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_bare_name_never_runs_a_program_from_the_working_directory()
    {
        Plant(scratch, "git");

        var ran = new Command("git", ["--version"], TimeSpan.FromSeconds(30)) { Directory = scratch }.Run();

        var exited = Assert.IsType<Ran.Exited>(ran);
        Assert.Equal(0, exited.Code);
        Assert.StartsWith("git version ", exited.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("planted", exited.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, exited.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_rooted_program_that_does_not_exist_and_a_cmd_or_bat_named_bare_are_NotFound_with_the_reason()
    {
        var missing = Assert.IsType<Ran.NotFound>(new Command(Path.Combine(scratch, "none" + (OperatingSystem.IsWindows() ? ".exe" : "")), [], TimeSpan.FromSeconds(10)).Run());
        var batch = Assert.IsType<Ran.NotFound>(new Command("estate-stand-in.cmd", [], TimeSpan.FromSeconds(10)).Run());

        Assert.Contains("cannot be started", missing.Why, StringComparison.Ordinal);
        Assert.Equal(OperatingSystem.IsWindows(), batch.Why.Contains("cmd.exe", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_program_past_its_timeout_is_TimedOut_carrying_what_it_wrote_and_every_process_it_started_has_ended()
    {
        var clock = Stopwatch.StartNew();

        var ran = new Command("dotnet", [Assembly, "spawn"], TimeSpan.FromSeconds(1)).Run();

        var timedOut = Assert.IsType<Ran.TimedOut>(ran);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "the timed-out run returned after " + clock.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(1), timedOut.Timeout);
        var child = int.Parse(timedOut.Output.Split('\n')[0].Trim(), CultureInfo.InvariantCulture);   // the child, sharing the pipe, prints its own id after its parent did
        Assert.True(SpinWait.SpinUntil(() => !Running(child), TimeSpan.FromSeconds(5)), "the child process " + child + " still runs after its parent was killed");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Output_and_errors_arrive_separately_whole_and_as_UTF_8_past_a_pipe_s_capacity()
    {
        const int Megabyte = 1 << 20;

        var flood = Assert.IsType<Ran.Exited>(new Command("dotnet", [Assembly, "flood", Megabyte.ToString(CultureInfo.InvariantCulture)], TimeSpan.FromMinutes(2)).Run());
        var utf8 = Assert.IsType<Ran.Exited>(new Command("dotnet", [Assembly, "utf8"], TimeSpan.FromMinutes(2)).Run());

        Assert.Equal((0, Megabyte, Megabyte), (flood.Code, flood.Output.Length, flood.Errors.Length));
        Assert.True(flood.Output.All(c => c == 'o') && flood.Errors.All(c => c == 'e'), "a stream carried the other's bytes");
        Assert.Equal(("Café-Ω\n", "Café-Ω\n"), (utf8.Output, utf8.Errors));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_cancelled_run_ends_the_program_and_throws_within_a_second_of_the_cancellation()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var clock = Stopwatch.StartNew();

        var thrown = Assert.Throws<OperationCanceledException>(() => new Command("dotnet", [Assembly, "sleep", "60"], TimeSpan.FromMinutes(1)).Run(cancel.Token));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(6), "the cancelled run threw after " + clock.Elapsed);
        Assert.Equal(cancel.Token, thrown.CancellationToken);
    }

    /// <summary>The run's interruption (Interruption) ends a command its caller passed no token to, and a cleanup command marked Interruptible = false still runs after it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_run_s_interruption_ends_a_command_given_no_token_and_a_cleanup_command_runs_after_it()
    {
        using var run = Interruption.Quiet();
        run.After(TimeSpan.FromMilliseconds(500));

        Assert.Throws<OperationCanceledException>(() => new Command("dotnet", [Assembly, "sleep", "60"], TimeSpan.FromMinutes(1)).Run());
        var cleanup = new Command("dotnet", [Assembly, "env", "PATH"], TimeSpan.FromMinutes(1)) { Interruptible = false }.Run();

        Assert.Equal("--timeout 0", run.Cause);
        Assert.Equal(0, Assert.IsType<Ran.Exited>(cleanup).Code);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_environment_given_is_applied_over_estate_s_own_and_a_null_value_removes_the_variable()
    {
        var (set, removed) = ("ESTATE_TEST_SET_" + Guid.NewGuid().ToString("N")[..8], "ESTATE_TEST_REMOVED_" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable(removed, "present");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { [set] = "given", [removed] = null };
        try
        {
            Assert.Equal("given", Assert.IsType<Ran.Exited>(new Command("dotnet", [Assembly, "env", set], TimeSpan.FromMinutes(1)) { Environment = environment }.Run()).Output.Trim());
            Assert.Equal("<unset>", Assert.IsType<Ran.Exited>(new Command("dotnet", [Assembly, "env", removed], TimeSpan.FromMinutes(1)) { Environment = environment }.Run()).Output.Trim());
            Assert.Equal("present", Assert.IsType<Ran.Exited>(new Command("dotnet", [Assembly, "env", removed], TimeSpan.FromMinutes(1)).Run()).Output.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable(removed, null);
        }
    }

    /// <summary>A program of the name planted in <paramref name="folder"/>: on Windows a copy of whoami.exe, elsewhere a script that prints "planted".</summary>
    internal static void Plant(string folder, string name)
    {
        if (OperatingSystem.IsWindows())
        {
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "whoami.exe"), Path.Combine(folder, name + ".exe"));
        }
        else
        {
            var script = Path.Combine(folder, name);
            File.WriteAllText(script, "#!/bin/sh\necho planted\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    internal static bool Running(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
