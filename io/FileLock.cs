using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Estate.Kernel;

namespace Estate.Io;

/// <summary>
/// The one way io takes a lock file (R6): the file opened for this process alone (<see cref="Take"/>), or for readers only
/// (<see cref="TakeShared"/>, which any number of processes hold at once and which keeps every exclusive taker out), until it is
/// disposed; the operating system releases it when the holder ends, however it ends. Another holder is waited out, twenty
/// milliseconds at a time, for the caller's timeout: a sharing violation is the only failure waited on (on Windows HResult
/// 0x80070020 or 0x80070021; on Linux and macOS the IOException .NET raises when flock reports EWOULDBLOCK, whose HResult is
/// that errno), and past the timeout it is lock.timed-out (exit 9). Any other failure to open the file, such as a file standing
/// where its folder should be or a read-only file system, is file.unwritable at once. The wait watches the caller's token and the
/// run's (Interruption), and throws OperationCanceledException within twenty milliseconds of either. On Linux and macOS with file
/// locking turned off in the runtime (DOTNET_SYSTEM_IO_DISABLEFILELOCKING, which makes .NET skip flock) every process would take
/// every lock at once, so a lock is refused as lock.unsupported there; Windows share modes do not depend on the switch.
/// </summary>
public sealed class FileLock : IDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    private readonly FileStream stream;

    private FileLock(string path, FileStream stream) => (Path, this.stream) = (path, stream);

    public string Path { get; }

    public static Result<FileLock> Take(string path, TimeSpan timeout, CancellationToken cancel = default) =>
        Taken(path, FileAccess.ReadWrite, FileShare.None, timeout, cancel);

    public static Result<FileLock> TakeShared(string path, TimeSpan timeout, CancellationToken cancel = default) =>
        Taken(path, FileAccess.Read, FileShare.Read, timeout, cancel);

    public void Dispose() => stream.Dispose();

    private static Result<FileLock> Taken(string path, FileAccess access, FileShare share, TimeSpan timeout, CancellationToken cancel)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows() && LockingOff())
        {
            return Unsupported;
        }

        var folder = System.IO.Path.GetDirectoryName(full)!;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (Write.FileSystemFailure(e))
        {
            return Write.Unwritable(folder, e);
        }

        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(cancel, Interruption.RunToken);
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileLock(full, new FileStream(full, FileMode.OpenOrCreate, access, share, bufferSize: 1));
            }
            catch (IOException e) when (Write.SharingViolation(e))
            {
                if (waiting.Elapsed >= timeout)
                {
                    return new Error("lock.timed-out", full + " is held by another estate process, and this one waited " + Command.Written(timeout) + " for it.",
                        "Wait for the other estate process to finish, or end it; its lock is released when it ends.");
                }

                if (interrupted.Token.WaitHandle.WaitOne(Poll))
                {
                    throw new OperationCanceledException("The run was interrupted while waiting for " + full + ".", cancel.IsCancellationRequested ? cancel : Interruption.RunToken);
                }
            }
            catch (Exception e) when (Write.FileSystemFailure(e))
            {
                return Write.Unwritable(full, e);
            }
        }
    }

    /// <summary>The refusal of a lock on Linux and macOS while the runtime's file locking is off; Windows never gives it, so the register's driver takes it from here.</summary>
    public static Error Unsupported => new Error("lock.unsupported",
        "File locking is turned off in this process (DOTNET_SYSTEM_IO_DISABLEFILELOCKING), so two estate processes could change " + LocalState.Name + "/ at once.",
        "Unset DOTNET_SYSTEM_IO_DISABLEFILELOCKING for estate, then run it again.");

    private static bool LockingOff() =>
        (AppContext.TryGetSwitch("System.IO.DisableFileLocking", out var off) && off)
        || Environment.GetEnvironmentVariable("DOTNET_SYSTEM_IO_DISABLEFILELOCKING") is { } set && (set == "1" || set.Equals("true", StringComparison.OrdinalIgnoreCase));
}
