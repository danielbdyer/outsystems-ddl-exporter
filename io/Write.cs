using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Estate.Io;

/// <summary>
/// Every byte the engine writes: UTF-8 without a byte-order mark; LF, unless the file already on disk
/// uses CRLF, whose line ending is kept; and a file replaced atomically, through a temporary file
/// beside it, so a reader sees the old content or the new and never a part.
/// </summary>
public static class Write
{
    private const int Attempts = 5;

    /// <summary>A lone surrogate has no UTF-8 form: it throws rather than becoming a question mark.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>, creating its directory.</summary>
    public static void Text(string path, string text)
    {
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var content = UsesCrlf(target) ? lf.Replace("\n", "\r\n", StringComparison.Ordinal) : lf;
        var temporary = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Path.GetRandomFileName() + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Text(stream, content);
                stream.Flush(flushToDisk: true);
            }

            Replace(temporary, target);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <summary>Writes <paramref name="text"/> to a stream, a file's or standard output, as UTF-8 without a byte-order mark.</summary>
    public static void Text(Stream stream, string text)
    {
        using var writer = new StreamWriter(stream, Utf8, bufferSize: 1 << 16, leaveOpen: true);
        writer.Write(text);
    }

    /// <summary>Whether the file on disk declares CRLF: its first line ends in CRLF.</summary>
    private static bool UsesCrlf(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        for (int previous = -1, current; (current = stream.ReadByte()) != -1; previous = current)
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
}
