using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>
/// estate/environments.json as a value (V3_MILESTONES.md WP 1.5, §4 row 14): the environments it names, each once, in name order, and the
/// local server it prefers, when it names one. io reads the file once for a verb and hands this to what resolves a target, to R15 and
/// to the lookup of a copy's profile.
/// </summary>
public sealed record Environments
{
    private Environments(SortedArray<NamedEnvironment> all, LocalServerKind? localServer) => (All, LocalServer) = (all, localServer);

    /// <summary>Every environment the environments file names, by name.</summary>
    public SortedArray<NamedEnvironment> All { get; }

    /// <summary>The local server the environments file prefers, the key localServer; null when it names none.</summary>
    public LocalServerKind? LocalServer { get; }

    /// <summary>The environments given, or environments.environment-name when two share a name; the error leads with <paramref name="subject"/>.</summary>
    public static Result<Environments> Of(string subject, IEnumerable<NamedEnvironment> environments, LocalServerKind? localServer)
    {
        var all = SortedArray.Of(environments);
        return all.Where((e, i) => i > 0 && all[i - 1].Name == e.Name).FirstOrDefault() is { } repeated
            ? new Error("environments.environment-name", subject + " names " + repeated.Target + " twice.", "Keep one entry for each environment.")
            : new Environments(all, localServer);
    }

    /// <summary>The environment the environments file names by <paramref name="name"/>, or null.</summary>
    public NamedEnvironment? Named(EnvironmentName name) => All.FirstOrDefault(e => e.Name == name);

    /// <summary>The publish profile every environment names, which a copy is planned under when no profile is named for it; null when two differ or none is named.</summary>
    public PublishProfilePath? SharedProfile => All.Select(e => e.Profile).Distinct().ToList() is [var shared] ? shared : null;
}

/// <summary>
/// Which SQL Server the environments file prefers to hold copies (VALUES.md O1): Docker, the estate-sql container; or LocalDb, where Docker cannot
/// run. The key localServer, written docker or localdb. The cases are closed.
/// </summary>
public abstract record LocalServerKind
{
    private LocalServerKind()
    {
    }

    /// <summary>The kind <paramref name="text"/> names, or environments.malformed led by <paramref name="subject"/>.</summary>
    public static Result<LocalServerKind> Of(string subject, string? text) => text switch
    {
        "docker" => new Docker(),
        "localdb" => new LocalDb(),
        _ => new Error("environments.malformed", subject + " is not docker or localdb.", "Write " + subject + " as docker or localdb."),
    };

    public T Match<T>(Func<T> docker, Func<T> localDb) => this switch
    {
        Docker => docker(),
        LocalDb => localDb(),
        _ => throw new UnreachableException(),
    };

    /// <summary>As the environments file writes it.</summary>
    public sealed override string ToString() => Match(() => "docker", () => "localdb");

    public sealed record Docker : LocalServerKind;

    public sealed record LocalDb : LocalServerKind;
}

/// <summary>
/// An environment as estate/environments.json names it (V3_MILESTONES.md WP 1.5, §4 row 14): its name; the host its SQL Server runs on, which
/// R15 compares with the local server's whether or not the environment's reference resolves on this machine (DECISIONS.md,
/// 2026-09-25); its classification and reader groups (the groups that may read it); the reference its connection resolves from; its publish
/// profile's path; its SQLCMD values; and, where the environments file names one, the metamodel's reference. Data only (§2.1 rule 3), holding no
/// value a reference names. Each error leads with the subject its caller gives, where in the environments file the value sits, and quotes no value.
/// </summary>
public sealed record NamedEnvironment : IComparable<NamedEnvironment>
{
    private NamedEnvironment(EnvironmentName name, Host host, Classification classification, SortedArray<string> readerGroups, SecretReference connection,
        PublishProfilePath profile, SortedArray<SqlCmdVariable> sqlCmd, SecretReference? metamodel) =>
        (Name, Host, Classification, ReaderGroups, Connection, Profile, SqlCmd, Metamodel) = (name, host, classification, readerGroups, connection, profile, sqlCmd, metamodel);

    /// <summary>The key estate/environments.json gives it: dev, qa, uat.</summary>
    public EnvironmentName Name { get; }

    /// <summary>The host its SQL Server runs on, as the environments file names it.</summary>
    public Host Host { get; }

    /// <summary>The target that names it, env:&lt;name&gt;.</summary>
    public Target Target => new Target.Environment(Name);

    public Classification Classification { get; }

    /// <summary>The groups that may read the environment, such as the Active Directory groups of its leads.</summary>
    public SortedArray<string> ReaderGroups { get; }

    public SecretReference Connection { get; }

    /// <summary>The pipeline's publish profile for the environment, from the estate's root.</summary>
    public PublishProfilePath Profile { get; }

    public SortedArray<SqlCmdVariable> SqlCmd { get; }

    public SecretReference? Metamodel { get; }

    /// <summary>An environment of the values given, or the error of a reader group blank or given twice, or of a SQLCMD variable given twice in any case.</summary>
    public static Result<NamedEnvironment> Of(string subject, EnvironmentName name, Host host, Classification classification, IEnumerable<string> readerGroups,
        SecretReference connection, PublishProfilePath profile, IEnumerable<SqlCmdVariable> sqlCmd, SecretReference? metamodel)
    {
        var (groups, values) = (SortedArray.Of(readerGroups), SortedArray.Of(sqlCmd));
        return groups.Where((g, i) => string.IsNullOrWhiteSpace(g) || g.Any(char.IsControl) || (i > 0 && groups[i - 1] == g)).Any()
                ? new Error("environments.reader-groups", subject + " names a reader group that is blank or given twice.", "Name each group that reads the environment once.")
            : values.Where((v, i) => i > 0 && values[i - 1].Name == v.Name).Any()
                ? new Error("environments.sqlcmd-repeated", subject + " gives one SQLCMD variable twice; sqlcmd reads names in any case as one.",
                    "Keep one value for each SQLCMD variable.")
            : new NamedEnvironment(name, host, classification, groups, connection, profile, values, metamodel);
    }

    /// <summary>By name, which the environments file gives each environment once.</summary>
    public int CompareTo(NamedEnvironment? other) => other is null ? 1 : Name.CompareTo(other.Name);

    public override string ToString() => Target + " (" + Classification + ")";
}

/// <summary>
/// A publish profile's path as the environments file gives it, from the estate's root: '/' between parts, none empty, '.' or '..', no ':', '\' or
/// control character, not led by '/', ending in .publish.xml; so it names a file inside the estate on every operating system.
/// default(PublishProfilePath) is not a path.
/// </summary>
public readonly record struct PublishProfilePath : IComparable<PublishProfilePath>
{
    private readonly string? _text;

    private PublishProfilePath(string text) => _text = text;

    /// <summary>The path <paramref name="text"/> gives, or environments.profile-path led by <paramref name="subject"/>.</summary>
    public static Result<PublishProfilePath> Of(string subject, string? text) => InsideTheEstate(text) ? new PublishProfilePath(text!)
        : new Error("environments.profile-path", subject + " gives its profile a path that leaves the estate or names no .publish.xml.",
            "Write the profile's path from the estate's root with '/' between its parts, such as estate/profiles/pipeline.publish.xml.");

    /// <summary>Ordinally.</summary>
    public int CompareTo(PublishProfilePath other) => string.CompareOrdinal(_text, other._text);

    public override string ToString() => _text ?? throw new InvalidOperationException("default(PublishProfilePath) is not a path; make one with PublishProfilePath.Of.");

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

/// <summary>A named lead's dated confirmation of an environment's classification: data the environments file commits, never a clock read.</summary>
public sealed record Confirmation
{
    private Confirmation(string lead, DateOnly on) => (Lead, On) = (lead, on);

    public string Lead { get; }

    public DateOnly On { get; }

    public static Result<Confirmation> Of(string subject, string? lead, string? on) =>
        string.IsNullOrWhiteSpace(lead) || lead.Length > 128 || lead.Any(char.IsControl) ? new Error("environments.confirmation",
            subject + " carries a confirmation with no lead's name on one line.", "Write confirmedBy as the name of the lead who confirmed the classification.")
        : !DateOnly.TryParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? new Error("environments.confirmation",
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
        "synthetic" => new Error("environments.unconfirmed", subject + " is marked synthetic with no lead's dated confirmation.",
            "Commit confirmedBy and confirmedOn beside it, the lead's name and the date, or mark it real."),
        _ => new Error("environments.classification", subject + " is classified as neither real nor synthetic.", "Write its classification as real or synthetic."),
    };

    public T Match<T>(Func<Real, T> real, Func<Synthetic, T> synthetic) =>
        this is Real r ? real(r) : this is Synthetic s ? synthetic(s) : throw new UnreachableException();

    public sealed override string ToString() => Match(r => r.Confirmation is null ? "real" : "real, " + r.Confirmation, s => "synthetic, " + s.Confirmation);

    public sealed record Real(Confirmation? Confirmation) : Classification;

    public sealed record Synthetic(Confirmation Confirmation) : Classification;
}
