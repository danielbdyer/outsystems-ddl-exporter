using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>
/// An environment as estate/posture.json names it (V3_MILESTONES.md WP 1.5, §4 row 14): its name, classification and cohorts, the
/// reference its connection resolves from, its publish profile's path from the estate's root, its SQLCMD values and the
/// metamodel's reference. Data only: it is read and never written (§2.1 rule 3), and holds no value a reference names. Each
/// refusal here leads with the subject its caller gives, where in the posture the value sits, and quotes no value.
/// </summary>
public sealed record NamedEnvironment : IComparable<NamedEnvironment>
{
    private static readonly Regex Named = new(@"\A[a-z][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant);

    private NamedEnvironment(string name, Classification classification, Seq<string> cohorts, SecretReference connection, string profilePath, Seq<SqlCmdVariable> sqlCmd, SecretReference? metamodel) =>
        (Name, Classification, Cohorts, Connection, ProfilePath, SqlCmd, Metamodel) = (name, classification, cohorts, connection, profilePath, sqlCmd, metamodel);

    /// <summary>1 to 32 lowercase letters, digits and hyphens, from a letter: dev, qa, uat.</summary>
    public string Name { get; }

    public Classification Classification { get; }

    public Seq<string> Cohorts { get; }

    public SecretReference Connection { get; }

    /// <summary>The pipeline's publish profile for this environment: a path from the estate's root, '/' between its parts.</summary>
    public string ProfilePath { get; }

    public Seq<SqlCmdVariable> SqlCmd { get; }

    /// <summary>The reference the OutSystems metamodel's database resolves from, where the posture names one.</summary>
    public SecretReference? Metamodel { get; }

    public static Result<NamedEnvironment> Of(
        string subject, string name, Classification classification, IEnumerable<string> cohorts, SecretReference connection, string profilePath, IEnumerable<SqlCmdVariable> sqlCmd, SecretReference? metamodel)
    {
        var (readers, values) = (Seq.Of(cohorts), Seq.Of(sqlCmd));
        return !Named.IsMatch(name) ? new Refusal("posture.environment-name", subject + " names an environment in other than 1 to 32 lowercase letters, digits and hyphens.", "Rename it with lowercase letters, digits and hyphens from a letter, such as dev or uat.")
            : readers.Where((c, i) => string.IsNullOrWhiteSpace(c) || c.Any(char.IsControl) || (i > 0 && readers[i - 1] == c)).Any()
                ? new Refusal("posture.cohort", subject + " names a cohort that is blank or given twice.", "Name each cohort that reads the environment once.")
            : !InsideTheEstate(profilePath)
                ? new Refusal("posture.profile-path", subject + " gives its profile a path that leaves the estate or names no .publish.xml.", "Write the profile's path from the estate's root with '/' between its parts, such as estate/profiles/pipeline.publish.xml.")
            : values.Where((v, i) => i > 0 && string.Equals(values[i - 1].Name, v.Name, StringComparison.OrdinalIgnoreCase)).Any()
                ? new Refusal("posture.sqlcmd-repeated", subject + " gives one SQLCMD variable twice; sqlcmd reads names in any case as one.", "Keep one value for each SQLCMD variable.")
            : new NamedEnvironment(name, classification, readers, connection, profilePath, values, metamodel);
    }

    /// <summary>By name, which the posture gives each environment once.</summary>
    public int CompareTo(NamedEnvironment? other) => string.CompareOrdinal(Name, other?.Name);

    public override string ToString() => "env:" + Name + " (" + Classification + ")";

    private static bool InsideTheEstate(string? path) =>
        path is { Length: > 0 } && path.EndsWith(".publish.xml", StringComparison.Ordinal) && !path.StartsWith('/')
        && !path.Any(c => char.IsControl(c) || c is '\\' or ':') && path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));
}

/// <summary>
/// Where a value the repository must never hold lives: an environment variable (env:NAME) or a file outside git (file:path). A
/// connection is always one, and so is every SQLCMD value not marked non-sensitive. It holds and prints the reference, never the
/// value, which io resolves where it connects; anything else, a connection string or a password, is refused unquoted.
/// </summary>
public sealed record SecretReference : IComparable<SecretReference>
{
    private const string VariablePrefix = "env:", FilePrefix = "file:";
    private static readonly Regex VariableName = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant);

    private SecretReference(string text) => Text = text;

    private string Text { get; }

    public static Result<SecretReference> Of(string subject, string? text) =>
        (text?.StartsWith(VariablePrefix, StringComparison.Ordinal) == true && VariableName.IsMatch(text[VariablePrefix.Length..]))
        || (text?.StartsWith(FilePrefix, StringComparison.Ordinal) == true && text[FilePrefix.Length..] is { Length: > 0 } file && file == file.Trim() && !file.Any(char.IsControl))
            ? new SecretReference(text)
            : new Refusal("reference.malformed", subject + " is neither env:NAME nor file:path.", "Write it as env:NAME, an environment variable, or file:path, a file outside git, and keep the value itself out of the repository.");

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
        string.IsNullOrWhiteSpace(lead) || lead.Length > 128 || lead.Any(char.IsControl)
            ? new Refusal("posture.confirmation", subject + " carries a confirmation with no lead's name on one line.", "Write confirmedBy as the name of the lead who confirmed the classification.")
        : !DateOnly.TryParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new Refusal("posture.confirmation", subject + " carries a confirmation with no date written as yyyy-MM-dd.", "Write confirmedOn as the date the lead confirmed it, such as 2026-09-20.")
        : new Confirmation(lead.Trim(), date);

    public override string ToString() => "confirmed by " + Lead + " on " + On.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Whether an environment's data is real or synthetic (V3_MILESTONES.md §17 item 2): real, which reads counts only, until a
/// named lead's dated confirmation says synthetic. A synthetic environment always carries that confirmation, the provenance
/// a vocabulary drawn from it cites; a real one carries a lead's confirmation when the posture gives one.
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
        "synthetic" => new Refusal("posture.unconfirmed", subject + " is marked synthetic with no lead's dated confirmation.", "Commit confirmedBy and confirmedOn beside it, the lead's name and the date, or mark it real."),
        _ => new Refusal("posture.classification", subject + " is classified as neither real nor synthetic.", "Write its classification as real or synthetic."),
    };

    public sealed record Real(Confirmation? Confirmation) : Classification
    {
        public override string ToString() => Confirmation is null ? "real" : "real, " + Confirmation;
    }

    public sealed record Synthetic(Confirmation Confirmation) : Classification
    {
        public override string ToString() => "synthetic, " + Confirmation;
    }
}

/// <summary>
/// A SQLCMD variable's value for an environment: a literal, committed only where it is marked non-sensitive, or a reference
/// to where the value lives. A name shaped like a credential (holding password, passwd, pwd, secret, token, key or credential,
/// in any case) never holds a literal. It prints its name and its reference, never a literal's text.
/// </summary>
public sealed record SqlCmdVariable : IComparable<SqlCmdVariable>
{
    private static readonly Regex Named = new(@"\A[A-Za-z_][A-Za-z0-9_-]{0,127}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Used = new(@"\$\((?<name>[A-Za-z_][A-Za-z0-9_-]{0,127})\)", RegexOptions.CultureInvariant);
    private static readonly string[] Credential = ["password", "passwd", "pwd", "secret", "token", "key", "credential"];

    private SqlCmdVariable(string name, string? literal, SecretReference? reference) => (Name, Literal, Reference) = (name, literal, reference);

    public string Name { get; }

    /// <summary>The value, when it is a literal; null when a reference names where it lives.</summary>
    public string? Literal { get; }

    public SecretReference? Reference { get; }

    public static Result<SqlCmdVariable> Of(string subject, string name, string literal) =>
        Unnamed(subject, name) is { } refusal ? refusal
        : Credential.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? new Refusal("sqlcmd.literal-credential", subject + " gives $(" + name + ") a literal, and a name shaped like a credential takes a reference.", "Give $(" + name + ") as env:NAME or file:path in the environment's sqlcmd in estate/posture.json, and delete the literal.")
        : new SqlCmdVariable(name, literal, null);

    public static Result<SqlCmdVariable> Of(string subject, string name, SecretReference reference) =>
        Unnamed(subject, name) is { } refusal ? refusal : new SqlCmdVariable(name, null, reference);

    /// <summary>
    /// A script with each $(name) replaced by its value, names matched as sqlcmd matches them, in any case, and each value put in
    /// once, never read for variables of its own. The substituted text exists only in the string returned: the script keeps its
    /// variables, and the kernel writes no file. A variable with no value is refused by its name, and no value is quoted.
    /// </summary>
    public static Result<string> Substitute(string script, IReadOnlyDictionary<string, string> values)
    {
        var byName = values.OrderBy(v => v.Key, StringComparer.Ordinal).DistinctBy(v => v.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(StringComparer.OrdinalIgnoreCase);
        return Used.Matches(script).Select(m => m.Groups["name"].Value).FirstOrDefault(name => !byName.ContainsKey(name)) is { } missing
            ? new Refusal("sqlcmd.undefined", "The script uses $(" + missing + "), and no SQLCMD value is given for it.", "Give " + missing + " a value in the environment's sqlcmd in estate/posture.json, or in the pipeline's profile.")
            : Used.Replace(script, m => byName[m.Groups["name"].Value]);
    }

    /// <summary>By name as sqlcmd reads it, in any case, so two names differing only in case sit side by side; then ordinally, then by value.</summary>
    public int CompareTo(SqlCmdVariable? other) => other is null ? 1 : ((int[])[string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase), string.CompareOrdinal(Name, other.Name),
        string.CompareOrdinal(Literal, other.Literal), string.CompareOrdinal(Reference?.ToString(), other.Reference?.ToString())]).FirstOrDefault(c => c != 0);

    public override string ToString() => "$(" + Name + ")" + (Reference is { } reference ? " from " + reference : ", a literal");

    private static Refusal? Unnamed(string subject, string name) => Named.IsMatch(name) ? null
        : new Refusal("sqlcmd.name", subject + " names a SQLCMD variable in other than letters, digits, '_' and '-' from a letter or '_'.", "Rename the variable as the project's SqlCmdVariable names it.");
}
