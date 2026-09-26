using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// io/FileLock, the one way io takes a lock file (R6): another process's hold is waited out and released when that process ends,
/// however it ends; a hold past the timeout is lock.timed-out; any other failure to open the file is file.unwritable at once; a
/// cancelled wait throws and takes nothing; shared holders coexist and keep an exclusive taker out.
/// </summary>
public sealed class FileLockTests : IDisposable
{
    private readonly ScratchFolder scratch = ScratchFolder.Temporary("lock");

    private string Lock => scratch.Under("state.lock");

    public void Dispose() => scratch.Dispose();

    [Fact]
    [Trait("Category", "fast")]
    public async Task A_lock_another_process_holds_is_taken_once_that_process_is_killed()
    {
        using var holder = DbChangeProcess.Start("lock", Lock);
        Assert.Equal("held", holder.StandardOutput.ReadLine());

        var taking = Task.Run(() => FileLock.Take(Lock, TimeSpan.FromSeconds(30)));
        await Task.Delay(TimeSpan.FromSeconds(1));
        var waited = !taking.IsCompleted;
        holder.Kill(entireProcessTree: true);
        var clock = Stopwatch.StartNew();
        using var taken = Value(await taking);

        Assert.True(waited, "the lock was taken while another process held it");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "the lock was taken " + clock.Elapsed + " after its holder was killed");
        Assert.Equal(Path.GetFullPath(Lock), taken.Path);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_lock_held_past_the_timeout_is_lock_timed_out_naming_the_file()
    {
        using var held = Value(FileLock.Take(Lock, TimeSpan.Zero));

        var error = Failed(FileLock.Take(Lock, TimeSpan.FromMilliseconds(200)), "lock.timed-out");

        Assert.Contains(Path.GetFullPath(Lock), error.Message, StringComparison.Ordinal);
        Assert.Contains("200 milliseconds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_lock_whose_folder_cannot_be_made_is_file_unwritable_at_once()
    {
        var file = scratch.File("file", "");
        var clock = Stopwatch.StartNew();

        var error = Failed(FileLock.Take(Path.Combine(file, "state.lock"), TimeSpan.FromMinutes(10)), "file.unwritable");

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "a lock whose folder cannot be made waited " + clock.Elapsed);
        Assert.Contains(file, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_cancelled_wait_throws_within_a_second_and_takes_nothing()
    {
        using var held = Value(FileLock.Take(Lock, TimeSpan.Zero));
        using var cancel = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var cancelledAt = TimeSpan.MaxValue;

        // A thread of its own cancels after 200 ms: a CancellationTokenSource's own timer runs on the thread pool, which the other test
        // classes' programs can hold for seconds on a CI runner, and the wait's response to its token is what this test measures.
        var canceller = new Thread(() =>
        {
            Thread.Sleep(200);
            cancelledAt = clock.Elapsed;
            cancel.Cancel();
        });
        canceller.Start();

        Assert.Throws<OperationCanceledException>(() => FileLock.Take(Lock, TimeSpan.FromMinutes(1), cancel.Token));

        canceller.Join();
        Assert.True(clock.Elapsed - cancelledAt < TimeSpan.FromSeconds(1), "the wait threw " + (clock.Elapsed - cancelledAt) + " after its token was cancelled");
        held.Dispose();
        using var taken = Value(FileLock.Take(Lock, TimeSpan.Zero));   // the cancelled wait left the lock free to take
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Shared_holders_coexist_and_keep_an_exclusive_taker_out_until_the_last_of_them_ends()
    {
        var one = Value(FileLock.TakeShared(Lock, TimeSpan.Zero));
        var two = Value(FileLock.TakeShared(Lock, TimeSpan.Zero));

        Failed(FileLock.Take(Lock, TimeSpan.Zero), "lock.timed-out");
        one.Dispose();
        Failed(FileLock.Take(Lock, TimeSpan.Zero), "lock.timed-out");
        two.Dispose();
        using var exclusive = Value(FileLock.Take(Lock, TimeSpan.Zero));
        Failed(FileLock.TakeShared(Lock, TimeSpan.Zero), "lock.timed-out");
    }

    /// <summary>
    /// DOTNET_SYSTEM_IO_DISABLEFILELOCKING makes .NET on Linux and macOS skip flock, so every process would take every lock at once:
    /// a holder started with it set is refused there (lock.unsupported), while on Windows, whose share modes ignore the switch, it holds
    /// the lock and this process's take times out.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void With_file_locking_turned_off_FileLock_refuses_on_Linux_and_macOS_and_still_excludes_on_Windows()
    {
        var start = new ProcessStartInfo("dotnet", [typeof(DbChangeProcess).Assembly.Location, "lock", Lock]) { RedirectStandardInput = true, RedirectStandardOutput = true };
        start.Environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = "1";
        using var holder = Process.Start(start)!;
        try
        {
            var said = holder.StandardOutput.ReadLine();
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("held", said);
                Failed(FileLock.Take(Lock, TimeSpan.FromMilliseconds(200)), "lock.timed-out");
            }
            else
            {
                Assert.StartsWith("lock.unsupported: ", said, StringComparison.Ordinal);
            }
        }
        finally
        {
            holder.Kill(entireProcessTree: true);
            holder.WaitForExit();   // the scratch folder's deletion raced the holder's handle on the Windows runner
        }
    }
}
