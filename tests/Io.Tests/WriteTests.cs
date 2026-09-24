using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>io/Write: UTF-8 without a BOM, LF unless the file on disk uses CRLF, replaced atomically. Temporary directories only.</summary>
public sealed class WriteTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("estate-write-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData(null, "a\r\nb\ncé\n", "a\nb\ncé\n")]  // a new file is LF
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

        Write.Text(path, text);

        Assert.Equal(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(expected), File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Category", "fast")]
    public async Task A_reader_sees_the_old_content_or_the_new_never_a_part()
    {
        var path = Path.Combine(directory, "file.json");
        string[] versions = [new string('a', 1 << 20) + "\n", new string('b', 3 << 19) + "\n"];
        Write.Text(path, versions[0]);
        using var done = new CancellationTokenSource();
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
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // the name is mid-replacement; an open that fails is not a partial read
                }
            }
        });

        for (var i = 1; i <= 40; i++)
        {
            Write.Text(path, versions[i % 2]);
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
        Write.Text(path, "old\n");

        // A lone surrogate has no UTF-8 form, so the write fails a megabyte into the new content.
        Assert.ThrowsAny<EncoderFallbackException>(() => Write.Text(path, new string('x', 1 << 20) + "\uD800y"));

        Assert.Equal("old\n", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }
}
