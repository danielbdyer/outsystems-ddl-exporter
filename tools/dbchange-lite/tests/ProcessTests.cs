using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class ProcessTests
{
    private static (string File, string[] Args) Sleeper => OperatingSystem.IsWindows() ? ("ping", ["-n", "30", "127.0.0.1"]) : ("sleep", ["30"]);

    [Fact]
    [Trait("Category", "fast")]
    public void An_interrupt_stops_a_child_process_within_seconds_instead_of_waiting_for_its_bound()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => Ssdt.Exec(Sleeper.File, Sleeper.Args, Path.GetTempPath(), TimeSpan.FromMinutes(5), cancel.Token));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Exec returned after " + clock.Elapsed);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_child_process_past_its_bound_is_killed_and_refused_as_a_timeout()
    {
        var stop = Assert.Throws<Stop>(() => Ssdt.Exec(Sleeper.File, Sleeper.Args, Path.GetTempPath(), TimeSpan.FromMilliseconds(300), CancellationToken.None));
        Assert.Equal((6, "process.timeout"), (stop.Outcome.Exit, stop.Finding.Code));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_merge_git_refuses_without_a_conflict_is_merge_failed_quoting_git_and_a_conflict_names_its_file()
    {
        using var scratch = new Scratch();
        Git(scratch.Path, "init", "-q", "-b", "main");
        scratch.Write("Estate/Customer.sql", "CREATE TABLE [dbo].[Customer] ([Id] INT NOT NULL);\n");
        Git(scratch.Path, "add", ".");
        Git(scratch.Path, "commit", "-q", "-m", "base");
        Git(scratch.Path, "checkout", "-q", "-b", "feature");
        scratch.Write("Estate/Customer.sql", "CREATE TABLE [dbo].[Customer] ([Id] INT NOT NULL, [Email] NVARCHAR (256) NULL);\n");
        Git(scratch.Path, "commit", "-q", "-am", "feature");
        Git(scratch.Path, "checkout", "-q", "main");
        scratch.Write("Estate/Customer.sql", "CREATE TABLE [dbo].[Customer] ([Id] BIGINT NOT NULL);\n");
        Git(scratch.Path, "commit", "-q", "-am", "main");
        Git(scratch.Path, "checkout", "-q", "--orphan", "unrelated");
        Git(scratch.Path, "commit", "-q", "-m", "unrelated");
        Git(scratch.Path, "checkout", "-q", "-f", "feature");
        var run = Runs.At(scratch.Path, "predict");

        var conflict = Assert.Throws<Stop>(() => Ssdt.Package(new Source.Ref("feature"), new Source.Ref("main"), run));
        Assert.Equal((8, "merge.conflict"), (conflict.Outcome.Exit, conflict.Finding.Code));
        Assert.Contains("Estate/Customer.sql", conflict.Finding.Message, StringComparison.Ordinal);

        var failed = Assert.Throws<Stop>(() => Ssdt.Package(new Source.Ref("feature"), new Source.Ref("unrelated"), run));
        Assert.Equal((8, "merge.failed"), (failed.Outcome.Exit, failed.Finding.Code));
        Assert.Contains("unrelated histories", failed.Finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("src-", Git(scratch.Path, "worktree", "list"), StringComparison.Ordinal);
    }

    private static string Git(string repository, params string[] args)
    {
        var result = Ssdt.Exec("git", ["-C", repository, "-c", "user.name=test", "-c", "user.email=test@localhost", "-c", "commit.gpgsign=false", .. args], repository, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(result.Code == 0, "git " + string.Join(' ', args) + ": " + result.Error);
        return result.Output;
    }
}
