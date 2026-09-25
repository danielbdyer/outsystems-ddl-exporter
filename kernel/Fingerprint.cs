using System;
using System.Buffers;
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

    /// <summary>
    /// The fingerprint of a model: its elements in the SortedArray's canonical order, serialized so no two element sets serialize
    /// alike. Every list leads with its count, every string with its length and is written as UTF-16 code units (a lone
    /// surrogate survives), every value with a tag, every name with its part count and every key with whether it has a
    /// parent; integers are big-endian. Text values are hashed exactly as the elements hold them, so two models
    /// fingerprint equally exactly when their elements are equal.
    /// </summary>
    public static Fingerprint Of(SortedArray<Element> elements)
    {
        var to = new ArrayBufferWriter<byte>();
        Write(to, elements.Count);
        foreach (var element in elements)
        {
            Write(to, element.Key);
            Write(to, element.Properties.Count);
            foreach (var property in element.Properties)
            {
                Write(to, property.Name);
                property.Value.Match(
                    b => Write(to, b ? 2 : 1),
                    n => Write(to, 3) + Write(to, n),
                    s => Write(to, 4) + Write(to, s),
                    (type, member) => Write(to, 5) + Write(to, type) + Write(to, member),
                    () => Write(to, 0));
            }

            Write(to, element.Relationships.Count);
            foreach (var relationship in element.Relationships)
            {
                Write(to, relationship.Name);
                Write(to, relationship.Targets.Count);
                foreach (var target in relationship.Targets)
                {
                    Write(to, target.Position);
                    Write(to, target.Key);
                }
            }
        }

        return Of(to.WrittenSpan);
    }

    /// <summary>A fingerprint from the 64 lowercase hex digits <see cref="ToString"/> renders, and from nothing else.</summary>
    public static Result<Fingerprint> Parse(string hex) =>
        hex is { Length: 64 } && hex.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            ? new Fingerprint(Convert.FromHexString(hex))
            : new Error(
                "fingerprint.malformed",
                $"'{hex}' is not a fingerprint.",
                "Give the fingerprint as estate prints it: 64 lowercase hex digits.");

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{_w0:x16}{_w1:x16}{_w2:x16}{_w3:x16}");

    private static ulong Word(ReadOnlySpan<byte> digest, int index) =>
        BinaryPrimitives.ReadUInt64BigEndian(digest.Slice(index * sizeof(ulong), sizeof(ulong)));

    // Each writer returns the bytes it wrote, so a value's tag and content write in one expression, left to right.
    private static int Write(ArrayBufferWriter<byte> to, int n)
    {
        BinaryPrimitives.WriteInt32BigEndian(to.GetSpan(sizeof(int)), n);
        to.Advance(sizeof(int));
        return sizeof(int);
    }

    private static int Write(ArrayBufferWriter<byte> to, long n)
    {
        BinaryPrimitives.WriteInt64BigEndian(to.GetSpan(sizeof(long)), n);
        to.Advance(sizeof(long));
        return sizeof(long);
    }

    private static int Write(ArrayBufferWriter<byte> to, string text)
    {
        var span = to.GetSpan(sizeof(int) + (text.Length * sizeof(char)));
        BinaryPrimitives.WriteInt32BigEndian(span, text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[(sizeof(int) + (i * sizeof(char)))..], text[i]);
        }

        to.Advance(sizeof(int) + (text.Length * sizeof(char)));
        return sizeof(int) + (text.Length * sizeof(char));
    }

    private static int Write(ArrayBufferWriter<byte> to, ElementKey key) =>
        (key.Parent is { } parent ? Write(to, 1) + Write(to, parent) : Write(to, 0))
        + Write(to, key.Type)
        + (key.Name.Schema is { } schema ? Write(to, 2) + Write(to, schema) : Write(to, 1))
        + Write(to, key.Name.Base);

    private static string Canonical(string text) =>
        (text.StartsWith('\uFEFF') ? text[1..] : text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
