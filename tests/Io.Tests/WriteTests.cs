using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/Write: UTF-8 without a BOM, the kernel's line ending (CRLF and a lone CR to LF) unless the file on disk declares CRLF, read through
/// its UTF-8 or UTF-16 byte-order mark; replaced atomically; a failure the file system reports is file.unwritable with the old content kept;
/// an appended file is on disk when Append returns. Temporary directories only.
/// </summary>
public sealed class WriteTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("estate-write-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData(null, "a\r\nb\ncé\n", "a\nb\ncé\n")]  // a new file is LF
    [InlineData(null, "a\rb\r\n", "a\nb\n")]           // a lone CR is a line break too, as Fingerprint reads it (R7)
    [InlineData("x\ny\n", "a\r\nb\n", "a\nb\n")]                 // a file on disk in LF stays LF
    [InlineData("x\r\ny\r\n", "a\nb\r\nc", "a\r\nb\r\nc")]      // a file on disk in CRLF stays CRLF
    [InlineData("﻿x\r\ny", "a\nb\n", "a\r\nb\r\n")]        // its CRLF is kept and its BOM is not
    public void Writes_utf8_without_a_bom_in_the_line_ending_on_disk(string? onDisk, string text, string expected)
    {
        var path = Path.Combine(directory, "file.sql");
        if (onDisk is not null)
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(onDisk));
        }

        Assert.Equal(path, Ok(Write.Text(path, text)));

        Assert.Equal(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(expected), File.ReadAllBytes(path));
    }

    /// <summary>
    /// Windows PowerShell 5's Out-File writes UTF-16 with a byte-order mark; there LF is the bytes 0A 00, so a byte scan for 0D before 0A
    /// read a CRLF file as LF. The line ending is read as UTF-16 code units, and the file is rewritten as UTF-8 in that ending (D3).
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(true, "x\r\ny", "a\r\nb\r\n")]
    [InlineData(false, "x\r\ny", "a\r\nb\r\n")]
    [InlineData(true, "x\ny", "a\nb\n")]
    public void A_UTF_16_file_is_rewritten_as_UTF_8_in_the_line_ending_it_declares(bool littleEndian, string onDisk, string expected)
    {
        var path = Path.Combine(directory, "file.ps1");
        var encoding = littleEndian ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true) : new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(onDisk)]);

        Ok(Write.Text(path, "a\nb\n"));

        Assert.Equal(new UTF8Encoding(false).GetBytes(expected), File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_write_where_a_file_stands_in_the_folder_s_place_is_file_unwritable_naming_the_folder()
    {
        var file = Path.Combine(directory, "file");
        File.WriteAllText(file, "");

        var error = Failed(Write.Text(Path.Combine(file, "under.txt"), "x\n"));

        Assert.Equal(("file.unwritable", 6), (error.Code, Cli.Contract.Exit(error)));
        Assert.StartsWith(file + " cannot be written: a file stands where a folder should be.", error.Message, StringComparison.Ordinal);
        Assert.Equal("", File.ReadAllText(file));
    }

    /// <summary>A target this identity may not replace, read-only on Windows and in a folder it may not write elsewhere: file.unwritable, the old content kept, no temporary file left.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_write_the_file_system_refuses_is_file_unwritable_and_leaves_the_old_content_and_no_temporary_file()
    {
        var path = Path.Combine(directory, "kept", "file.sql");
        Ok(Write.Text(path, "old\n"));
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }
        else
        {
            File.SetUnixFileMode(Path.GetDirectoryName(path)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        var error = Failed(Write.Text(path, "new\n"));

        Assert.Equal("file.unwritable", error.Code);
        Assert.Contains("this identity may not write it, or it is read-only", error.Message, StringComparison.Ordinal);
        Assert.Equal("old\n", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_appended_entry_is_on_disk_when_Append_returns_and_a_reader_sees_every_earlier_entry()
    {
        var path = Path.Combine(directory, "runs", "queries.log");

        Assert.Equal(path, Ok(Write.Append(path, "-- one\nSELECT 1;\nGO\n")));
        var first = Read(path);
        Ok(Write.Append(path, "-- two\r\nSELECT 2;\r\nGO\r\n"));

        Assert.Equal("-- one\nSELECT 1;\nGO\n", first);
        Assert.Equal("-- one\nSELECT 1;\nGO\n-- two\nSELECT 2;\nGO\n", Read(path));
    }

    /// <summary>A second Ctrl-C, SIGKILL or a power loss can leave a write's temporary file beside its target; the next write of that target removes one older than an hour and keeps a fresh one, which a write in progress may own.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_temporary_file_an_ended_write_left_is_removed_by_the_next_write_of_its_target_and_a_fresh_one_is_kept()
    {
        var path = Path.Combine(directory, "file.json");
        var (stale, fresh) = (Path.Combine(directory, ".file.json.stale.tmp"), Path.Combine(directory, ".file.json.fresh.tmp"));
        File.WriteAllText(stale, "");
        File.WriteAllText(fresh, "");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

        Ok(Write.Text(path, "{}\n"));

        Assert.Equal([fresh, path], Directory.GetFiles(directory).Order(StringComparer.Ordinal));
    }

    /// <summary>The file read while Append's stream may still be open on it, as the log's reader does.</summary>
    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static T Ok<T>(Kernel.Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Kernel.Error Failed<T>(Kernel.Result<T> result) => Assert.IsType<Kernel.Result<T>.Failed>(result).Error;

    [Fact]
    [Trait("Category", "fast")]
    public async Task A_reader_sees_the_old_content_or_the_new_never_a_part()
    {
        var path = Path.Combine(directory, "file.json");
        string[] versions = [new string('a', 1 << 20) + "\n", new string('b', 3 << 19) + "\n"];
        Ok(Write.Text(path, versions[0]));
        using var done = new CancellationTokenSource();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (whole, torn) = (0, 0);
        var reader = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var text = new StreamReader(stream);
                    var seen = text.ReadToEnd();
                    _ = seen == versions[0] || seen == versions[1] ? whole++ : torn++;
                    reading.TrySetResult();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // the name is mid-replacement; an open that fails is not a partial read
                }
            }
        });

        // The writes start once the reader reads, or a busy machine can finish all forty before the reader is scheduled.
        await reading.Task.WaitAsync(TimeSpan.FromMinutes(1));
        for (var i = 1; i <= 40; i++)
        {
            Ok(Write.Text(path, versions[i % 2]));
        }

        await done.CancelAsync();
        await reader;
        Assert.True(whole > 0, "the reader never read the file");
        Assert.Equal(0, torn);
        Assert.Equal(versions[0], File.ReadAllText(path));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_failed_write_leaves_the_old_content_and_no_temporary_file()
    {
        var path = Path.Combine(directory, "file.sql");
        Ok(Write.Text(path, "old\n"));

        // A lone surrogate has no UTF-8 form, so the write fails a megabyte into the new content.
        Assert.ThrowsAny<EncoderFallbackException>(() => Write.Text(path, new string('x', 1 << 20) + "\uD800y"));

        Assert.Equal("old\n", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }
}
