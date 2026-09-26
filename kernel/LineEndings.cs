using System;

namespace DbChange.Kernel;

/// <summary>
/// The kernel's one line-ending rule, XML 1.0's end-of-line handling (section 2.11): CRLF and a lone CR become LF. DacFx applies
/// it to every value it reads from a model, Fingerprint applies it before hashing text, and io/Write applies it before it chooses
/// a file's line ending, so a text checked out on Windows and the same text checked out on Linux read, hash and write alike.
/// </summary>
public static class LineEndings
{
    public static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
