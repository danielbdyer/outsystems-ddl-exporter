using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Estate.Kernel;

/// <summary>
/// SHA-256 over canonical bytes, rendered as 64 lowercase hex digits: the identity of an input (a delta, a target,
/// the data facts, a profile) that every process on every machine computes alike. Text is made canonical first: a
/// leading byte-order mark is dropped, CRLF and a lone CR become LF (XML 1.0's end-of-line rule), and the text is
/// encoded as UTF-8 with no byte-order mark (a lone surrogate becomes U+FFFD), so one text checked out on Windows
/// or on Linux fingerprints alike. Bytes are hashed as given. The digest is held as four words, so comparing two
/// never allocates; default(Fingerprint) is the all-zero digest, which no known input produces.
/// </summary>
public readonly record struct Fingerprint
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly ulong _w0, _w1, _w2, _w3; // the digest, big-endian

    private Fingerprint(ReadOnlySpan<byte> digest) =>
        (_w0, _w1, _w2, _w3) = (Word(digest, 0), Word(digest, 1), Word(digest, 2), Word(digest, 3));

    public static Fingerprint Of(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return new Fingerprint(digest);
    }

    public static Fingerprint Of(string text) => Of(Utf8.GetBytes(Canonical(text)));

    /// <summary>A fingerprint from the 64 lowercase hex digits <see cref="ToString"/> renders, and from nothing else.</summary>
    public static Result<Fingerprint> Parse(string hex) =>
        hex is { Length: 64 } && hex.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            ? new Fingerprint(Convert.FromHexString(hex))
            : new Refusal(
                "fingerprint.malformed",
                $"'{hex}' is not a fingerprint.",
                "Give the fingerprint as a receipt prints it: 64 lowercase hex digits.");

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{_w0:x16}{_w1:x16}{_w2:x16}{_w3:x16}");

    private static ulong Word(ReadOnlySpan<byte> digest, int index) =>
        BinaryPrimitives.ReadUInt64BigEndian(digest.Slice(index * sizeof(ulong), sizeof(ulong)));

    private static string Canonical(string text) =>
        (text.StartsWith('\uFEFF') ? text[1..] : text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
