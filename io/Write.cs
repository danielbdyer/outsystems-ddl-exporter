using System;
using System.IO;
using System.Text;
using System.Threading;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// Every byte dbchange writes to a file (VALUES.md D3): UTF-8 without a byte-order mark; the kernel's line ending (LineEndings.Lf:
/// CRLF and a lone CR to LF), unless the file already on disk declares CRLF by its first line, read through a UTF-8 or UTF-16
/// byte-order mark, in which case CRLF is kept and the file is still rewritten as UTF-8; and a file replaced atomically, through a
/// temporary file beside it, so a reader sees the old content or the new and never a part. A failure the file system reports is
/// file.unwritable (exit 6), naming the path and the cause: a folder that cannot be made, a full disk, a read-only file or file
/// system, a target another program holds open past a few attempts. A lone surrogate in the text has no UTF-8 form and throws, since it
/// is a defect in the caller's text; the old content is kept and no temporary file is left. A target that is a symbolic link is replaced by
/// a regular file, and a target with other hard links leaves those names on the old content. Text(Stream, string) writes standard output
/// and keeps throwing, since Program.Run answers for that stream.
/// </summary>
public static class Write
{
    private const int Attempts = 5;

    /// <summary>A lone surrogate has no UTF-8 form: it throws rather than becoming a question mark.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>, creating its folder; the full path written.</summary>
    public static Result<string> Text(string path, string text)
    {
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e) when (FileSystemFailure(e))
        {
            return Unwritable(directory, e);
        }

        var lf = LineEndings.Lf(text);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Path.GetRandomFileName() + ".tmp");
        try
        {
            Stale(directory, Path.GetFileName(target));
            var content = UsesCrlf(target) ? lf.Replace("\n", "\r\n", StringComparison.Ordinal) : lf;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Text(stream, content);
                stream.Flush(flushToDisk: true);
            }

            Replace(temporary, target);
            return target;
        }
        catch (Exception e) when (FileSystemFailure(e))
        {
            Discard(temporary);
            return Unwritable(target, e);
        }
        catch
        {
            Discard(temporary);
            throw;
        }
    }

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="path"/>, creating the file and its folder, as UTF-8 in LF, flushed to the
    /// operating system when it returns and shared with readers meanwhile; no fsync, since an appended file is a record of what ran
    /// (a run's queries.log), never a document a person keeps. The full path written.
    /// </summary>
    public static Result<string> Append(string path, string text)
    {
        var target = Path.GetFullPath(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var stream = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.Read);
            Text(stream, LineEndings.Lf(text));
            stream.Flush();
            return target;
        }
        catch (Exception e) when (FileSystemFailure(e))
        {
            return Unwritable(target, e);
        }
    }

    /// <summary>Writes <paramref name="text"/> to a stream, a file's or standard output, as UTF-8 without a byte-order mark.</summary>
    public static void Text(Stream stream, string text)
    {
        using var writer = new StreamWriter(stream, Utf8, bufferSize: 1 << 16, leaveOpen: true);
        writer.Write(text);
    }

    /// <summary>Whether an exception is one the file system reports about a path, which becomes file.unwritable, rather than a defect in the caller.</summary>
    internal static bool FileSystemFailure(Exception e) => e is IOException or UnauthorizedAccessException or NotSupportedException;

    /// <summary>Whether an IOException says another process holds the file: on Windows a sharing or lock violation, on Linux and macOS flock's EWOULDBLOCK.</summary>
    internal static bool SharingViolation(IOException e) => OperatingSystem.IsWindows()
        ? e.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
        : e.HResult is 11 or 35;

    /// <summary>file.unwritable for <paramref name="path"/>, its cause read from the exception the file system raised, with the remedy for that cause.</summary>
    internal static Error Unwritable(string path, Exception cause)
    {
        var (what, remedy) = cause switch
        {
            IOException io when DiskFull(io) => ("the disk is full", "Free space on the disk that holds " + path + ", then run dbchange again."),
            IOException io when SharingViolation(io) => ("another program holds it open", "Close the program that holds " + path + " open, then run dbchange again."),
            IOException io when FileInTheWay(io) => ("a file stands where a folder should be", "Move the file that stands where the folder of " + path + " should be, then run dbchange again."),
            UnauthorizedAccessException => ("this identity may not write it, or it is read-only", "Grant this identity write access to " + path + ", or clear its read-only attribute, then run dbchange again."),
            _ => (cause.Message.TrimEnd('.'), "Fix what the operating system reports for " + path + ", then run dbchange again."),
        };
        return new Error("file.unwritable", path + " cannot be written: " + what + ".", remedy);
    }

    /// <summary>ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL on Windows; ENOSPC elsewhere.</summary>
    private static bool DiskFull(IOException e) => OperatingSystem.IsWindows() ? e.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027) : e.HResult == 28;

    /// <summary>ERROR_ALREADY_EXISTS on Windows, which CreateDirectory raises for a file of the folder's name; ENOTDIR or EEXIST elsewhere.</summary>
    private static bool FileInTheWay(IOException e) => OperatingSystem.IsWindows() ? e.HResult == unchecked((int)0x800700B7) : e.HResult is 20 or 17;

    /// <summary>Whether the file on disk declares CRLF: its first line ends in CRLF, read as UTF-16 when it opens with that byte-order mark.</summary>
    private static bool UsesCrlf(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var (first, second) = (stream.ReadByte(), stream.ReadByte());
        var (utf16, littleEndian) = ((first, second) is (0xFF, 0xFE) or (0xFE, 0xFF), first == 0xFF);
        if (!utf16)
        {
            stream.Position = 0;
        }

        int Next()
        {
            var (low, high) = (stream.ReadByte(), utf16 ? stream.ReadByte() : 0);
            return low < 0 || high < 0 ? -1 : utf16 ? (littleEndian ? low | (high << 8) : (low << 8) | high) : low;
        }

        for (int previous = -1, current; (current = Next()) != -1; previous = current)
        {
            if (current == '\n')
            {
                return previous == '\r';
            }
        }

        return false;
    }

    /// <summary>
    /// Puts the temporary file in the target's place. File.Replace keeps the target's attributes and, on
    /// Windows, succeeds while a reader that shares delete holds the old file open; File.Move would not.
    /// A reader that does not share delete, or a scanner, is waited out for a few attempts.
    /// </summary>
    private static void Replace(string temporary, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!File.Exists(target))
                {
                    File.Move(temporary, target, overwrite: true);
                    return;
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(target));
                }

                File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception e) when (attempt < Attempts && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }

    /// <summary>
    /// Temporary files a write of the target left beside it, more than an hour ago (a second Ctrl-C, SIGKILL or a power loss cut that
    /// write off), deleted before this write makes its own; a fresh one may belong to a write in progress and stays.
    /// </summary>
    private static void Stale(string directory, string name)
    {
        foreach (var left in Directory.EnumerateFiles(directory, "." + name + ".*.tmp"))
        {
            if (File.GetLastWriteTimeUtc(left) < DateTime.UtcNow.AddHours(-1))
            {
                Discard(left);
            }
        }
    }

    /// <summary>
    /// A file dbchange made for itself deleted: a failed write's temporary file, a commit's temporary index, a gone worktree's holders' lock.
    /// One the file system will not release (a scanner holds it) is left, and the result being reported stays the one reported.
    /// </summary>
    internal static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (FileSystemFailure(e))
        {
            // a scanner holds it; the next run's write, commit or sweep meets it again
        }
    }
}
