using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Estate.Kernel;
using Xunit;
using Contract = Estate.Cli.Contract;

namespace Estate.Io.Tests;

/// <summary>
/// io/FileLock, the one way io takes a lock file (R6): another process's hold is waited out and released when that process ends,
/// however it ends; a hold past the timeout is lock.timed-out at exit 9; any other failure to open the file is file.unwritable at
/// once; a cancelled wait throws and takes nothing; shared holders coexist and keep an exclusive taker out.
/// </summary>
public sealed class FileLockTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("estate-lock-").FullName;

    private string Lock => Path.Combine(scratch, "state.lock");

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    [Trait("Category", "fast")]
    public async Task A_lock_another_process_holds_is_taken_once_that_process_is_killed()
    {
        using var holder = EstateProcess.Start("lock", Lock);
        Assert.Equal("held", holder.StandardOutput.ReadLine());

        var taking = Task.Run(() => FileLock.Take(Lock, TimeSpan.FromSeconds(30)));
        await Task.Delay(TimeSpan.FromSeconds(1));
        var waited = !taking.IsCompleted;
        holder.Kill(entireProcessTree: true);
        var clock = Stopwatch.StartNew();
        using var taken = Ok(await taking);

        Assert.True(waited, "the lock was taken while another process held it");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "the lock was taken " + clock.Elapsed + " after its holder was killed");
        Assert.Equal(Path.GetFullPath(Lock), taken.Path);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_lock_held_past_the_timeout_is_lock_timed_out_at_exit_9_naming_the_file()
    {
        using var held = Ok(FileLock.Take(Lock, TimeSpan.Zero));

        var error = Failed(FileLock.Take(Lock, TimeSpan.FromMilliseconds(200)));

        Assert.Equal(("lock.timed-out", 9), (error.Code, Contract.Exit(error)));
        Assert.Contains(Path.GetFullPath(Lock), error.Message, StringComparison.Ordinal);
        Assert.Contains("200 milliseconds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_lock_whose_folder_cannot_be_made_is_file_unwritable_at_once()
    {
        var file = Path.Combine(scratch, "file");
        File.WriteAllText(file, "");
        var clock = Stopwatch.StartNew();

        var error = Failed(FileLock.Take(Path.Combine(file, "state.lock"), TimeSpan.FromMinutes(10)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "a lock whose folder cannot be made waited " + clock.Elapsed);
        Assert.Equal(("file.unwritable", 6), (error.Code, Contract.Exit(error)));
        Assert.Contains(file, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_cancelled_wait_throws_within_a_second_and_takes_nothing()
    {
        using var held = Ok(FileLock.Take(Lock, TimeSpan.Zero));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();

        Assert.Throws<OperationCanceledException>(() => FileLock.Take(Lock, TimeSpan.FromMinutes(1), cancel.Token));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), "the cancelled wait threw after " + clock.Elapsed);   // 200 ms alone; other classes start programs beside this one
        held.Dispose();
        using var taken = Ok(FileLock.Take(Lock, TimeSpan.Zero));   // the cancelled wait left the lock free to take
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Shared_holders_coexist_and_keep_an_exclusive_taker_out_until_the_last_of_them_ends()
    {
        var one = Ok(FileLock.TakeShared(Lock, TimeSpan.Zero));
        var two = Ok(FileLock.TakeShared(Lock, TimeSpan.Zero));

        Assert.Equal("lock.timed-out", Failed(FileLock.Take(Lock, TimeSpan.Zero)).Code);
        one.Dispose();
        Assert.Equal("lock.timed-out", Failed(FileLock.Take(Lock, TimeSpan.Zero)).Code);
        two.Dispose();
        using var exclusive = Ok(FileLock.Take(Lock, TimeSpan.Zero));
        Assert.Equal("lock.timed-out", Failed(FileLock.TakeShared(Lock, TimeSpan.Zero)).Code);
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
        var start = new ProcessStartInfo("dotnet", [typeof(EstateProcess).Assembly.Location, "lock", Lock]) { RedirectStandardInput = true, RedirectStandardOutput = true };
        start.Environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = "1";
        using var holder = Process.Start(start)!;
        try
        {
            var said = holder.StandardOutput.ReadLine();
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("held", said);
                Assert.Equal("lock.timed-out", Failed(FileLock.Take(Lock, TimeSpan.FromMilliseconds(200))).Code);
            }
            else
            {
                Assert.StartsWith("lock.unsupported: ", said, StringComparison.Ordinal);
            }
        }
        finally
        {
            holder.Kill(entireProcessTree: true);
        }
    }

    private static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}
