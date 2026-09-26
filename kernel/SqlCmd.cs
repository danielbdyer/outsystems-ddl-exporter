using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>
/// A SQLCMD variable's value, in one of two closed cases Match alone reads: a literal, committed only where marked non-sensitive, or a
/// reference to where the value lives. A name shaped like a credential (holding password, passwd, pwd, secret, token, key or credential,
/// in any case) never holds a literal. It prints its name and its reference, never a literal. Beside it, the rest of the SQLCMD grammar a
/// deploy script is written in, on the one name pattern: the placeholder $(name), substitution, the :setvar lines a kept script leaves
/// out, and a script without its sqlcmd directives.
/// </summary>
public abstract record SqlCmdVariable : IComparable<SqlCmdVariable>
{
    /// <summary>A variable's name as sqlcmd reads one: a letter or '_', then up to 127 letters, digits, '_' and '-'; io/SchemaText reads a placeholder by it too.</summary>
    public const string NamePattern = "[A-Za-z_][A-Za-z0-9_-]{0,127}";

    private static readonly Regex Used = new(@"\$\((?<name>" + NamePattern + @")\)", RegexOptions.CultureInvariant);
    private static readonly Regex SetVar = new(@"\A[ \t]*:setvar[ \t]+(?<name>" + NamePattern + @")(?:[ \t\r\n]|\z)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] Credential = ["password", "passwd", "pwd", "secret", "token", "key", "credential"];

    private SqlCmdVariable(SqlCmdName name) => Name = name;

    public SqlCmdName Name { get; }

    public static Result<SqlCmdVariable> Of(string subject, string name, string literal) => SqlCmdName.Of(subject, name).Bind(named =>
        Credential.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? new Error("sqlcmd.literal-credential", subject + " gives " + Placeholder(name) + " a literal, and a name shaped like a credential takes a reference.",
                "Give " + Placeholder(name) + " as env:NAME or file:path in the environment's sqlcmd in estate/environments.json, and delete the literal.")
            : Result.Ok<SqlCmdVariable>(new Literal(named, literal)));

    public static Result<SqlCmdVariable> Of(string subject, string name, SecretReference reference) =>
        SqlCmdName.Of(subject, name).Map(named => (SqlCmdVariable)new Referenced(named, reference));

    /// <summary>How a script and a message write the variable <paramref name="name"/>: $(name).</summary>
    public static string Placeholder(string name) => "$(" + name + ")";

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
            ? new Error("sqlcmd.undefined", "The script uses " + Placeholder(missing) + ", and no SQLCMD value is given for it.",
                "Give " + missing + " a value in the environment's sqlcmd in estate/environments.json, or in the pipeline's profile.")
            : Used.Replace(script, m => byName[m.Groups["name"].Value]);
    }

    /// <summary>
    /// A script without the :setvar line of each variable <paramref name="names"/> holds, names and the command matched in any case as
    /// sqlcmd matches them, so a value a reference gave never reaches a script kept on disk. Every other line stays as it was, its line
    /// end included, and a :setvar with no value goes alone.
    /// </summary>
    public static string Unset(string script, IEnumerable<string> names)
    {
        var unset = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return unset.Count == 0 ? script : string.Concat(Lines(script).Where(line => !(SetVar.Match(line) is { Success: true } setVar && unset.Contains(setVar.Groups["name"].Value))));
    }

    /// <summary>
    /// A script without sqlcmd's own lines, those whose first character other than a space or a tab is ':' (:setvar, :r, :on error,
    /// :connect), so what is left is T-SQL a parser reads; every other line stays as it was, GO included.
    /// </summary>
    public static string WithoutDirectives(string script) => string.Concat(Lines(script).Where(line => !line.TrimStart(' ', '\t').StartsWith(':')));

    /// <summary>The literal's text, or the reference to where the value lives.</summary>
    public T Match<T>(Func<string, T> literal, Func<SecretReference, T> reference) =>
        this is Literal l ? literal(l.Text) : this is Referenced r ? reference(r.Reference) : throw new UnreachableException();

    /// <summary>By name as sqlcmd reads it, in any case, as the name's equality has it; then by value.</summary>
    public int CompareTo(SqlCmdVariable? other) => other is null ? 1 : Name.CompareTo(other.Name) is var c and not 0 ? c : string.CompareOrdinal(Sorted(this), Sorted(other));

    public sealed override string ToString() => Placeholder(Name.ToString()) + Match(_ => ", a literal", reference => " from " + reference);

    /// <summary>A script's lines, each with its own line end (LF, or CRLF), so the lines joined again are the script.</summary>
    private static IEnumerable<string> Lines(string script)
    {
        for (var start = 0; start < script.Length;)
        {
            var end = script.IndexOf('\n', start) is var at and >= 0 ? at + 1 : script.Length;
            yield return script[start..end];
            start = end;
        }
    }

    private static string Sorted(SqlCmdVariable variable) => variable.Match(text => "0" + text, reference => "1" + reference);

    private sealed record Literal(SqlCmdName Name, string Text) : SqlCmdVariable(Name);

    private sealed record Referenced(SqlCmdName Name, SecretReference Reference) : SqlCmdVariable(Name);
}

/// <summary>
/// A SQLCMD variable's name, as a project's SqlCmdVariable, a publish profile, estate/environments.json and a package's declarations
/// (DacPackage.SqlCmdVariables) name one, on the one name pattern. sqlcmd matches names in any case, so two names differing only in
/// case are one name: equal, ordered as one, and hashed alike. It prints as it was written. default(SqlCmdName) is not a name.
/// </summary>
public readonly struct SqlCmdName : IEquatable<SqlCmdName>, IComparable<SqlCmdName>
{
    private static readonly Regex Named = new(@"\A" + SqlCmdVariable.NamePattern + @"\z", RegexOptions.CultureInvariant);

    private readonly string? _text;

    private SqlCmdName(string text) => _text = text;

    public static bool operator ==(SqlCmdName left, SqlCmdName right) => left.Equals(right);

    public static bool operator !=(SqlCmdName left, SqlCmdName right) => !left.Equals(right);

    /// <summary>The name <paramref name="name"/> gives, or sqlcmd.name led by <paramref name="subject"/>.</summary>
    public static Result<SqlCmdName> Of(string subject, string? name) => name is not null && Named.IsMatch(name) ? new SqlCmdName(name)
        : new Error("sqlcmd.name", subject + " names a SQLCMD variable in other than letters, digits, '_' and '-' from a letter or '_'.",
            "Rename the variable as the project's SqlCmdVariable names it.");

    public bool Equals(SqlCmdName other) => string.Equals(_text, other._text, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is SqlCmdName other && Equals(other);

    public override int GetHashCode() => _text is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_text);

    /// <summary>Ignoring case, as sqlcmd matches names, so the order agrees with equality.</summary>
    public int CompareTo(SqlCmdName other) => string.Compare(_text, other._text, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => _text ?? throw new InvalidOperationException("default(SqlCmdName) is not a name; make one with SqlCmdName.Of.");
}
