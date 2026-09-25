using System;
using System.Globalization;

namespace Estate.Kernel;

/// <summary>
/// The name of a SQL Server object as the engine reads it: one part (a schema, a column, an index) or two (a
/// schema-qualified object, <c>[dbo].[Customer]</c>). Two is the most a Name holds and the most it needs: the
/// aggregate-query allowlist admits one or two parts, and an object inside a table (a column, an index, a constraint) is named by its
/// table's Name and its own one-part Name, so <c>[dbo].[Customer].[Email]</c> is a path of two Names that an
/// element composes. Each part is 1 to 128 UTF-16 code units (sysname), whatever the units are: SQL Server admits white
/// space and control characters inside brackets (<c>[ ] INT NULL</c> builds with no warning), so a schema it holds is read whole,
/// and a control character is escaped where text leaves the tool (decision 2.25). Parts are held unquoted and compared ordinally
/// with case, so the kernel never loses a difference; a case-insensitive match, as SQL Server's usual collation makes one, is
/// <see cref="Matches"/> under a <see cref="Collation"/>, the caller's explicit choice (decision 2.26). <see cref="ToString"/> quotes
/// each part as QUOTENAME does. default(Name) is not a name.
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

    public static Result<Name> Of(string part) => Invalid(part) is { } error ? error : new Name(null, part);

    public static Result<Name> Of(string schema, string part) =>
        (Invalid(schema) ?? Invalid(part)) is { } error ? error : new Name(schema, part);

    /// <summary>One-part names first, then by schema, then by the last part; all ordinal.</summary>
    public int CompareTo(Name other)
    {
        var bySchema = string.CompareOrdinal(Schema, other.Schema);
        return bySchema != 0 ? bySchema : string.CompareOrdinal(_base, other._base);
    }

    /// <summary>
    /// Whether two names are one name under <paramref name="collation"/>, as DacFx's deploy plan reads them: both parts equal
    /// ordinally, or, under a case-insensitive collation, equal ignoring case. Equality and order stay ordinal, so a case-only
    /// difference is never lost and the fingerprint's bytes never move.
    /// </summary>
    public bool Matches(Name other, Collation collation) =>
        string.Equals(Schema, other.Schema, collation.Comparison) && string.Equals(_base, other._base, collation.Comparison);

    public override string ToString() => Schema is null ? Quote(_base) : Quote(Schema) + "." + Quote(_base);

    private static string Quote(string? part) => "[" + part?.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static Error? Invalid(string? part) => part switch
    {
        null or "" => new Error("name.blank", "A name part is empty.", "Give each part 1 to 128 characters."),
        { Length: > Sysname } => new Error(
            "name.too-long",
            string.Create(CultureInfo.InvariantCulture, $"A name part has {part.Length} characters; SQL Server allows 128."),
            "Shorten the part to 128 characters."),
        _ => null,
    };
}

/// <summary>
/// A SQL Server collation by its name, read for what a deploy plan needs of it: whether names compare with case (decision 2.26).
/// DacFx compares object names under the model's collation, so <c>[dbo].[Customer]</c> and <c>[dbo].[customer]</c> are one table
/// under <c>SQL_Latin1_General_CP1_CI_AS</c> (measured: a case-only rename of a table on such a database plans nothing) and two under
/// <c>Latin1_General_CS_AS</c> or a binary collation. Accent, kana and width sensitivity are read and not applied: a pair differing in
/// accents alone still reads as two names, until an estate with such a collation is known (S8). <see cref="CaseSensitive"/> is the
/// comparison that follows no database: ordinal, as the kernel's own. default(Collation) is not a collation.
/// </summary>
public readonly record struct Collation : IComparable<Collation>
{
    /// <summary>The comparison that follows no database: names match exactly when they are equal, as the kernel compares them.</summary>
    public static readonly Collation CaseSensitive = new("Latin1_General_BIN2", caseSensitive: true, accentSensitive: true);

    private readonly string? _name;

    private Collation(string name, bool caseSensitive, bool accentSensitive) => (_name, IsCaseSensitive, IsAccentSensitive) = (name, caseSensitive, accentSensitive);

    /// <summary>The collation's name as SQL Server spells it, such as SQL_Latin1_General_CP1_CI_AS.</summary>
    public string Name => _name ?? throw new InvalidOperationException("default(Collation) is not a collation; make one with Collation.Of.");

    public bool IsCaseSensitive { get; }

    /// <summary>Read from the name (_AS, _AI; a binary collation is sensitive) and not applied to any comparison.</summary>
    public bool IsAccentSensitive { get; }

    /// <summary>How two name parts compare under this collation: ordinally, or ignoring case.</summary>
    public StringComparison Comparison => IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// A collation from its name: letters, digits and underscores, whose tokens say the case rule (_CS or _CI; _BIN and _BIN2 are
    /// case-sensitive). A name with no such token, such as Latin1_General alone, is the error model.collation.
    /// </summary>
    public static Result<Collation> Of(string? name)
    {
        var tokens = name is null ? [] : name.Split('_');
        var wellFormed = name is { Length: > 0 and <= 128 } && Array.TrueForAll(name.ToCharArray(), c => char.IsAsciiLetterOrDigit(c) || c == '_');
        var binary = Array.Exists(tokens, t => t is "BIN" or "BIN2");
        var caseRule = binary || Array.Exists(tokens, t => t == "CS") ? true : Array.Exists(tokens, t => t == "CI") ? false : (bool?)null;
        var accentRule = binary || Array.Exists(tokens, t => t == "AS") ? true : Array.Exists(tokens, t => t == "AI") ? false : caseRule;
        return wellFormed && caseRule is { } sensitive && accentRule is { } accents
            ? new Collation(name!, sensitive, accents)
            : new Error("model.collation", "'" + name + "' is not a SQL Server collation name that says whether names compare with case (_CS, _CI, _BIN or _BIN2).",
                "Report the model's source: a model's DatabaseOptions.Collation names the collation its objects compare under.");
    }

    public int CompareTo(Collation other) => string.CompareOrdinal(_name, other._name);

    public override string ToString() => Name;
}
