using System;
using System.Globalization;
using System.Linq;

namespace Estate.Kernel;

/// <summary>
/// The name of a SQL Server object as the engine reads it: one part (a schema, a column, an index) or two (a
/// schema-qualified object, <c>[dbo].[Customer]</c>). Two is the most a Name holds and the most it needs: the probe
/// grammar allows one or two parts, and an object inside a table (a column, an index, a constraint) is named by its
/// table's Name and its own one-part Name, so <c>[dbo].[Customer].[Email]</c> is a path of two Names that an
/// element composes. Each part is 1 to 128 characters (sysname), not all white space, with no control character.
/// Parts are held unquoted and compared ordinally with case, so the kernel never loses a difference; a
/// case-insensitive match, as SQL Server's usual collation makes one, is the caller's explicit choice.
/// <see cref="ToString"/> quotes each part as QUOTENAME does. default(Name) is not a name.
/// </summary>
public readonly record struct Name : IComparable<Name>
{
    private const int Sysname = 128;
    private readonly string? _base;

    private Name(string? schema, string @base) => (Schema, _base) = (schema, @base);

    /// <summary>The schema of a two-part name; null for a one-part name.</summary>
    public string? Schema { get; }

    /// <summary>The last part (ScriptDom's base identifier): the object, column or schema itself.</summary>
    public string Base => _base ?? throw new InvalidOperationException("default(Name) is not a name; make one with Name.Of.");

    public static Result<Name> Of(string part) => Refuse(part) is { } refusal ? refusal : new Name(null, part);

    public static Result<Name> Of(string schema, string part) =>
        (Refuse(schema) ?? Refuse(part)) is { } refusal ? refusal : new Name(schema, part);

    /// <summary>One-part names first, then by schema, then by the last part; all ordinal.</summary>
    public int CompareTo(Name other)
    {
        var bySchema = string.CompareOrdinal(Schema, other.Schema);
        return bySchema != 0 ? bySchema : string.CompareOrdinal(_base, other._base);
    }

    public override string ToString() => Schema is null ? Quote(_base) : Quote(Schema) + "." + Quote(_base);

    private static string Quote(string? part) => "[" + part?.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static Refusal? Refuse(string? part) => part switch
    {
        _ when string.IsNullOrWhiteSpace(part) =>
            new Refusal("name.blank", "A name part is empty or white space.", "Give each part 1 to 128 characters."),
        { Length: > Sysname } => new Refusal(
            "name.too-long",
            string.Create(CultureInfo.InvariantCulture, $"A name part has {part.Length} characters; SQL Server allows 128."),
            "Shorten the part to 128 characters."),
        _ when part.Any(char.IsControl) => new Refusal(
            "name.control-character",
            string.Create(CultureInfo.InvariantCulture, $"A name part holds the control character U+{(int)part.First(char.IsControl):X4}."),
            "Remove the control character from the part."),
        _ => null,
    };
}
