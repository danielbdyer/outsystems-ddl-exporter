using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>
/// An environment as estate/posture.json names it (V3_MILESTONES.md WP 1.5, §4 row 14): its name, classification and readers (the groups that may read it), the
/// reference its connection resolves from, its publish profile's path from the estate's root ('/' between its parts), its SQLCMD
/// values and, where the posture names one, the metamodel's reference. Data only (§2.1 rule 3), holding no value a reference names.
/// Each error leads with the subject its caller gives, where in the posture the value sits, and quotes no value.
/// </summary>
public sealed record NamedEnvironment : IComparable<NamedEnvironment>
{
    private NamedEnvironment(EnvironmentName name, Classification classification, SortedArray<string> readers, SecretReference connection, string profilePath,
        SortedArray<SqlCmdVariable> sqlCmd, SecretReference? metamodel) => (Name, Classification, Readers, Connection, ProfilePath, SqlCmd, Metamodel) =
        (name, classification, readers, connection, profilePath, sqlCmd, metamodel);

    /// <summary>The key estate/posture.json gives it: dev, qa, uat.</summary>
    public EnvironmentName Name { get; }

    /// <summary>The target that names it, env:&lt;name&gt;.</summary>
    public Target Target => new Target.Environment(Name);

    public Classification Classification { get; }

    /// <summary>The groups that may read the environment, such as the Active Directory groups of its leads.</summary>
    public SortedArray<string> Readers { get; }

    public SecretReference Connection { get; }

    public string ProfilePath { get; }

    public SortedArray<SqlCmdVariable> SqlCmd { get; }

    public SecretReference? Metamodel { get; }

    public static Result<NamedEnvironment> Of(string subject, string name, Classification classification, IEnumerable<string> readers,
        SecretReference connection, string profilePath, IEnumerable<SqlCmdVariable> sqlCmd, SecretReference? metamodel)
    {
        var (groups, values) = (SortedArray.Of(readers), SortedArray.Of(sqlCmd));
        return EnvironmentName.Of(subject, name).Bind(environment =>
            groups.Where((g, i) => string.IsNullOrWhiteSpace(g) || g.Any(char.IsControl) || (i > 0 && groups[i - 1] == g)).Any()
                ? new Error("posture.readers", subject + " names a reader group that is blank or given twice.", "Name each group that reads the environment once.")
            : !InsideTheEstate(profilePath)
                ? new Error("posture.profile-path", subject + " gives its profile a path that leaves the estate or names no .publish.xml.",
                    "Write the profile's path from the estate's root with '/' between its parts, such as estate/profiles/pipeline.publish.xml.")
            : values.Where((v, i) => i > 0 && string.Equals(values[i - 1].Name, v.Name, StringComparison.OrdinalIgnoreCase)).Any()
                ? new Error("posture.sqlcmd-repeated", subject + " gives one SQLCMD variable twice; sqlcmd reads names in any case as one.",
                    "Keep one value for each SQLCMD variable.")
            : Result.Ok(new NamedEnvironment(environment, classification, groups, connection, profilePath, values, metamodel)));
    }

    /// <summary>By name, which the posture gives each environment once.</summary>
    public int CompareTo(NamedEnvironment? other) => other is null ? 1 : Name.CompareTo(other.Name);

    public override string ToString() => Target + " (" + Classification + ")";

    private static bool InsideTheEstate(string? path) =>
        path is { Length: > 0 } && path.EndsWith(".publish.xml", StringComparison.Ordinal) && !path.StartsWith('/')
        && !path.Any(c => char.IsControl(c) || c is '\\' or ':') && path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));
}

/// <summary>
/// Where a value the repository must never hold lives: an environment variable (env:NAME) or a file outside git (file:path), which
/// io/SqlServer reads only when git ignores it or it is in no repository, and, on Linux and macOS, when its owner alone can read it. It holds
/// and prints the reference, never the value, which io resolves where it connects; anything else, a connection string or a password,
/// file: before it or not, is refused unquoted, since a path holds no '=' or ';'. A class, as Error is: a struct's default would
/// be a reference to nothing.
/// </summary>
public sealed record SecretReference : IComparable<SecretReference>
{
    private const string VariablePrefix = "env:", FilePrefix = "file:";
    private static readonly Regex VariableName = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant);

    private SecretReference(string text) => Text = text;

    private string Text { get; }

    public static Result<SecretReference> Of(string subject, string? text) =>
        (text?.StartsWith(VariablePrefix, StringComparison.Ordinal) == true && VariableName.IsMatch(text[VariablePrefix.Length..]))
        || (text?.StartsWith(FilePrefix, StringComparison.Ordinal) == true && text[FilePrefix.Length..] is { Length: > 0 } path
            && path == path.Trim() && !path.Any(c => char.IsControl(c) || c is '=' or ';'))
            ? new SecretReference(text)
            : new Error("reference.malformed", subject + " is neither env:NAME nor file:path; a path holds no '=' or ';', the marks of a connection string.",
                "Write it as env:NAME, an environment variable, or file:path, a file outside git, and keep the value itself out of the repository.");

    /// <summary>The environment variable's name, or the file's path, as the reference gives it.</summary>
    public T Match<T>(Func<string, T> variable, Func<string, T> file) =>
        Text.StartsWith(VariablePrefix, StringComparison.Ordinal) ? variable(Text[VariablePrefix.Length..]) : file(Text[FilePrefix.Length..]);

    public int CompareTo(SecretReference? other) => string.CompareOrdinal(Text, other?.Text);

    public override string ToString() => Text;
}

/// <summary>A named lead's dated confirmation of an environment's classification: data the posture commits, never a clock read.</summary>
public sealed record Confirmation
{
    private Confirmation(string lead, DateOnly on) => (Lead, On) = (lead, on);

    public string Lead { get; }

    public DateOnly On { get; }

    public static Result<Confirmation> Of(string subject, string? lead, string? on) =>
        string.IsNullOrWhiteSpace(lead) || lead.Length > 128 || lead.Any(char.IsControl) ? new Error("posture.confirmation",
            subject + " carries a confirmation with no lead's name on one line.", "Write confirmedBy as the name of the lead who confirmed the classification.")
        : !DateOnly.TryParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? new Error("posture.confirmation",
            subject + " carries a confirmation with no date written as yyyy-MM-dd.", "Write confirmedOn as the date the lead confirmed it, such as 2026-09-20.")
        : new Confirmation(lead.Trim(), date);

    public override string ToString() => "confirmed by " + Lead + " on " + On.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Whether an environment's data is real or synthetic (V3_MILESTONES.md §17 item 2): real, which reads counts only, until a named lead's
/// dated confirmation says synthetic. Synthetic always carries it, the provenance a vocabulary drawn from it cites; real, when given.
/// </summary>
public abstract record Classification
{
    private Classification()
    {
    }

    public static Result<Classification> Of(string subject, string? claimed, Confirmation? confirmation) => claimed switch
    {
        null or "real" => new Real(confirmation),
        "synthetic" when confirmation is not null => new Synthetic(confirmation),
        "synthetic" => new Error("posture.unconfirmed", subject + " is marked synthetic with no lead's dated confirmation.",
            "Commit confirmedBy and confirmedOn beside it, the lead's name and the date, or mark it real."),
        _ => new Error("posture.classification", subject + " is classified as neither real nor synthetic.", "Write its classification as real or synthetic."),
    };

    public T Match<T>(Func<Real, T> real, Func<Synthetic, T> synthetic) =>
        this is Real r ? real(r) : this is Synthetic s ? synthetic(s) : throw new UnreachableException();

    public sealed override string ToString() => Match(r => r.Confirmation is null ? "real" : "real, " + r.Confirmation, s => "synthetic, " + s.Confirmation);

    public sealed record Real(Confirmation? Confirmation) : Classification;

    public sealed record Synthetic(Confirmation Confirmation) : Classification;
}

/// <summary>
/// A SQLCMD variable's value, in one of two closed cases Match alone reads: a literal, committed only where marked non-sensitive, or a
/// reference to where the value lives. A name shaped like a credential (holding password, passwd, pwd, secret, token, key or credential,
/// in any case) never holds a literal. It prints its name and its reference, never a literal.
/// </summary>
public abstract record SqlCmdVariable : IComparable<SqlCmdVariable>
{
    private static readonly Regex Named = new(@"\A[A-Za-z_][A-Za-z0-9_-]{0,127}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Used = new(@"\$\((?<name>[A-Za-z_][A-Za-z0-9_-]{0,127})\)", RegexOptions.CultureInvariant);
    private static readonly string[] Credential = ["password", "passwd", "pwd", "secret", "token", "key", "credential"];

    private SqlCmdVariable(string name) => Name = name;

    public string Name { get; }

    public static Result<SqlCmdVariable> Of(string subject, string name, string literal) =>
        Unnamed(subject, name) is { } error ? error
        : Credential.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? new Error("sqlcmd.literal-credential", subject + " gives $(" + name + ") a literal, and a name shaped like a credential takes a reference.",
                "Give $(" + name + ") as env:NAME or file:path in the environment's sqlcmd in estate/posture.json, and delete the literal.")
        : new Literal(name, literal);

    public static Result<SqlCmdVariable> Of(string subject, string name, SecretReference reference) =>
        Unnamed(subject, name) is { } error ? error : new Referenced(name, reference);

    /// <summary>
    /// A script with each $(name) replaced by its value, names matched in any case as sqlcmd matches them, and each value put in once,
    /// never read for variables of its own. The text exists only in the string returned: the script keeps its variables, and the kernel
    /// writes no file. A variable with no value is the error sqlcmd.undefined, which names the variable and quotes no value.
    /// </summary>
    public static Result<string> Substitute(string script, IReadOnlyDictionary<string, string> values)
    {
        var byName = values.OrderBy(v => v.Key, StringComparer.Ordinal).DistinctBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(StringComparer.OrdinalIgnoreCase);
        return Used.Matches(script).Select(m => m.Groups["name"].Value).FirstOrDefault(name => !byName.ContainsKey(name)) is { } missing
            ? new Error("sqlcmd.undefined", "The script uses $(" + missing + "), and no SQLCMD value is given for it.",
                "Give " + missing + " a value in the environment's sqlcmd in estate/posture.json, or in the pipeline's profile.")
            : Used.Replace(script, m => byName[m.Groups["name"].Value]);
    }

    /// <summary>The literal's text, or the reference to where the value lives.</summary>
    public T Match<T>(Func<string, T> literal, Func<SecretReference, T> reference) =>
        this is Literal l ? literal(l.Text) : this is Referenced r ? reference(r.Reference) : throw new UnreachableException();

    /// <summary>By name as sqlcmd reads it, in any case, so two names differing only in case sit side by side; then ordinally, then by value.</summary>
    public int CompareTo(SqlCmdVariable? other) => other is null ? 1 : ((int[])[string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase),
        string.CompareOrdinal(Name, other.Name), string.CompareOrdinal(Sorted(this), Sorted(other))]).FirstOrDefault(c => c != 0);

    public sealed override string ToString() => "$(" + Name + ")" + Match(_ => ", a literal", reference => " from " + reference);

    private static string Sorted(SqlCmdVariable variable) => variable.Match(text => "0" + text, reference => "1" + reference);

    private static Error? Unnamed(string subject, string name) => Named.IsMatch(name) ? null
        : new Error("sqlcmd.name", subject + " names a SQLCMD variable in other than letters, digits, '_' and '-' from a letter or '_'.",
            "Rename the variable as the project's SqlCmdVariable names it.");

    private sealed record Literal(string Name, string Text) : SqlCmdVariable(Name);

    private sealed record Referenced(string Name, SecretReference Reference) : SqlCmdVariable(Name);
}
