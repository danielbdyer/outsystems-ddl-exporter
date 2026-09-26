using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DbChange.Kernel;

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
                    s => Write(to, 6) + Write(to, s),
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

    /// <summary>
    /// The fingerprint of a deploy report: its operations in the SortedArray's order, each by whether its kind is unlisted, its kind's
    /// name, its key and its issues' ids; then its alerts, each by whether its kind is unlisted, its kind's name, whether it has an id and
    /// the id, and its text; every list led by its count and every string by its length. So two reports fingerprint alike exactly when they
    /// are equal, whatever order DacFx listed their operations in.
    /// </summary>
    public static Fingerprint Of(DeployReport report)
    {
        var to = new ArrayBufferWriter<byte>();
        Write(to, report.Operations.Count);
        foreach (var operation in report.Operations)
        {
            Write(to, operation.Kind is PlanOperationKind.Unlisted ? 1 : 0);
            Write(to, operation.Kind.Name);
            Write(to, operation.Key);
            Write(to, operation.Issues.Count);
            foreach (var issue in operation.Issues)
            {
                Write(to, issue);
            }
        }

        Write(to, report.Alerts.Count);
        foreach (var alert in report.Alerts)
        {
            Write(to, alert.Kind is PlanAlertKind.Unlisted ? 1 : 0);
            Write(to, alert.Kind.Name);
            _ = alert.Id is { } id ? Write(to, 1) + Write(to, id) : Write(to, 0);
            Write(to, alert.Text);
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
                "Give the fingerprint as dbchange prints it: 64 lowercase hex digits.");

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

    /// <summary>A text as it is hashed: without a byte-order mark, its line endings LF (LineEndings.Lf).</summary>
    private static string Canonical(string text) => LineEndings.Lf(text.StartsWith('\uFEFF') ? text[1..] : text);
}
