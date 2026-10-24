using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DbChange.Kernel;

/// <summary>
/// Where a verb reads or writes, as an argument names it (V3_MILESTONES.md WP 1.4): env:&lt;name&gt;, an environment
/// dbchange/environments.json names; copy:&lt;name&gt;, a copy .dbchange/copies.json holds; synthetic-copy, the synthetic copy; ref:&lt;git
/// ref&gt;, the project at a ref; and dacpac:&lt;path&gt;, a package. The first three are databases and the last two are packages. The
/// cases are closed, and each prints as the argument that names it. A literal connection string where a target goes is refused in io, which
/// reads SqlClient's grammar.
/// </summary>
public abstract record Target : IComparable<Target>
{
    private Target()
    {
    }

    /// <summary>
    /// The target <paramref name="text"/> names, read for the argument <paramref name="subject"/>: copy: before a name dbchange never gives
    /// a copy is copy.unregistered; any other text in no form of the grammar, env: before a name no environment can have included, is
    /// target.unknown. Neither quotes the text.
    /// </summary>
    public static Result<Target> Parse(string text, string subject) =>
        text == "synthetic-copy" ? new SyntheticCopy()
        : After(text, "env:") is { } name && EnvironmentName.Of(subject, name) is Result<EnvironmentName>.Ok(var environment)
            ? new Environment(environment)
        : After(text, "copy:") is { } copy ? CopyName.Of(subject, copy).Map(Target (made) => new RegisteredCopy(made))
        : After(text, "ref:") is { } reference && Kernel.GitRef.Of(subject, reference) is Result<Kernel.GitRef>.Ok(var at) ? new GitRef(at)
        : After(text, "dacpac:") is { Length: > 0 } path && !path.Any(char.IsControl) ? new Dacpac(path)
        : new Error("target.unknown",
            "The value of " + subject + " is none of env:<name>, copy:<name>, synthetic-copy, ref:<git ref> and dacpac:<path>.",
            "Write the target in one of those forms, such as env:dev or ref:main.");

    public T Match<T>(Func<Environment, T> environment, Func<RegisteredCopy, T> registeredCopy, Func<T> syntheticCopy,
        Func<GitRef, T> gitRef, Func<Dacpac, T> dacpac) => this switch
    {
        Environment e => environment(e),
        RegisteredCopy c => registeredCopy(c),
        SyntheticCopy => syntheticCopy(),
        GitRef r => gitRef(r),
        Dacpac d => dacpac(d),
        _ => throw new UnreachableException(),
    };

    /// <summary>Ordinally, by the argument that names it.</summary>
    public int CompareTo(Target? other) => string.CompareOrdinal(ToString(), other?.ToString());

    public sealed override string ToString() =>
        Match(e => "env:" + e.Name, c => "copy:" + c.Name, () => "synthetic-copy", r => "ref:" + r.Ref, d => "dacpac:" + d.Path);

    private static string? After(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : null;

    /// <summary>An environment dbchange/environments.json names, read only.</summary>
    public sealed record Environment(EnvironmentName Name) : Target;

    /// <summary>A copy dbchange made and .dbchange/copies.json records; the database it names is io/SqlServer's Copy.</summary>
    public sealed record RegisteredCopy(CopyName Name) : Target;

    /// <summary>The synthetic copy, a copy filled with rows generated from the measured data (M3).</summary>
    public sealed record SyntheticCopy : Target;

    /// <summary>The project as a git ref holds it, built into a package.</summary>
    public sealed record GitRef(Kernel.GitRef Ref) : Target;

    /// <summary>A package on disk, by its path.</summary>
    public sealed record Dacpac(string Path) : Target;
}

/// <summary>
/// An environment's name, as dbchange/environments.json keys it and env: names it: 1 to 32 lowercase letters, digits and hyphens, from a
/// letter, such as dev, qa or uat-2. The one grammar of the name. default(EnvironmentName) is not a name.
/// </summary>
public readonly record struct EnvironmentName : IComparable<EnvironmentName>
{
    private static readonly Regex Grammar = new(@"\A[a-z][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant);

    private readonly string? _text;

    private EnvironmentName(string text) => _text = text;

    /// <summary>The name <paramref name="text"/> gives, or environments.environment-name led by <paramref name="subject"/>, the text
    /// unquoted.</summary>
    public static Result<EnvironmentName> Of(string subject, string? text) =>
        text is not null && Grammar.IsMatch(text) ? new EnvironmentName(text)
        : new Error("environments.environment-name",
            subject + " names an environment in other than 1 to 32 lowercase letters, digits and hyphens.",
            "Rename it with lowercase letters, digits and hyphens from a letter, such as dev or uat.");

    /// <summary>Ordinally.</summary>
    public int CompareTo(EnvironmentName other) => string.CompareOrdinal(_text, other._text);

    public override string ToString() =>
        _text ?? throw new InvalidOperationException("default(EnvironmentName) is not a name; make one with EnvironmentName.Of.");
}

/// <summary>
/// A copy's database name, as dbchange makes one and copy: names one (DECISIONS.md, 2026-09-25), in one of two forms. A run's copy is
/// dbchange_&lt;host&gt;_&lt;pid&gt;_&lt;hex&gt;, the name of the machine that made it in lowercase letters, digits and '_' (1 to 40), the
/// id of the process that made it, and eight hexadecimal digits; the machine and the process let a later run drop the copies of a process
/// that ended on this machine. The synthetic copy is dbchange_synthetic_&lt;16 hex&gt;_existing-data[_&lt;16 hex&gt;] (V3_MILESTONES.md §4
/// row 13): the first sixteen hexadecimal digits of its base's fingerprint, over the package, the profile, DacFx and the server, and of the
/// fingerprint of the existing data planted in it, where one is; it names no machine and no process, so Machine is empty and Pid 0 for
/// it, and no sweep chooses it. A run's name never takes the synthetic form, which holds a '-'. The one grammar of the name.
/// default(CopyName) is not a name.
/// </summary>
public readonly record struct CopyName : IComparable<CopyName>
{
    private static readonly Regex Grammar = new(@"\Adbchange_(?<machine>[a-z0-9_]{1,40})_(?<pid>0|[1-9][0-9]{0,9})_[0-9a-f]{8}\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex SyntheticGrammar =
        new(@"\Adbchange_synthetic_(?<base>[0-9a-f]{16})_existing-data(?:_(?<data>[0-9a-f]{16}))?\z", RegexOptions.CultureInvariant);

    private readonly string? _text;

    private CopyName(string text, string machine, int pid) => (_text, Machine, Pid) = (text, machine, pid);

    /// <summary>The name of the machine that made a run's copy, as the copy's name carries it; empty for the synthetic copy.</summary>
    public string Machine { get; }

    /// <summary>The id of the process that made a run's copy; 0 for the synthetic copy.</summary>
    public int Pid { get; }

    /// <summary>Whether the name is the synthetic copy's, dbchange_synthetic_&lt;16 hex&gt;_existing-data[_&lt;16 hex&gt;].</summary>
    public bool IsSynthetic => _text is not null && SyntheticGrammar.IsMatch(_text);

    /// <summary>The synthetic copy's first sixteen hexadecimal digits of its base's fingerprint; null for a run's copy.</summary>
    public string? Base => _text is not null && SyntheticGrammar.Match(_text) is { Success: true } m ? m.Groups["base"].Value : null;

    /// <summary>The synthetic copy's first sixteen hexadecimal digits of its existing data's fingerprint; null for a synthetic copy that
    /// holds presence rows alone, and for a run's copy.</summary>
    public string? ExistingData => _text is not null && SyntheticGrammar.Match(_text) is { Success: true } m && m.Groups["data"].Success
        ? m.Groups["data"].Value : null;

    /// <summary>The name <paramref name="text"/> gives, or copy.unregistered led by <paramref name="subject"/>, the text unquoted:
    /// .dbchange/copies.json holds no copy by a name dbchange never gives one.</summary>
    public static Result<CopyName> Of(string subject, string? text) =>
        text is not null && Grammar.Match(text) is { Success: true } match
            && int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            ? new CopyName(text, match.Groups["machine"].Value, pid)
        : text is not null && SyntheticGrammar.IsMatch(text) ? new CopyName(text, "", 0)
        : new Error("copy.unregistered",
            subject + " names a copy by a name dbchange never gives one, so .dbchange/copies.json holds no copy by it;"
            + " dbchange names a copy dbchange_<host>_<pid>_<hex>, for the machine and the process that made it,"
            + " in lowercase letters, digits and '_', and the synthetic copy dbchange_synthetic_<16 hex>_existing-data[_<16 hex>].",
            "Name a copy that .dbchange/copies.json holds on this machine.");

    /// <summary>
    /// The synthetic copy's name for a base and the existing data planted in it: dbchange_synthetic_, the first sixteen hexadecimal digits
    /// of <paramref name="base"/>, _existing-data, and, where <paramref name="existingData"/> is given, _ and its first sixteen; 66
    /// characters at most, well within the 128 a database name holds.
    /// </summary>
    public static CopyName Synthetic(Fingerprint @base, Fingerprint? existingData) =>
        new("dbchange_synthetic_" + @base.ToString()[..16] + "_existing-data"
            + (existingData is { } data ? "_" + data.ToString()[..16] : ""), "", 0);

    /// <summary>
    /// The name a copy made by process <paramref name="pid"/> on the machine <paramref name="machineName"/> takes, with
    /// <paramref name="suffix"/> as its eight hexadecimal digits: the machine's name in lower case, each character outside a to z and 0
    /// to 9 written '_', cut to forty ('_' for a machine with no name). The caller draws the suffix at random, since the kernel reads no
    /// random source.
    /// </summary>
    public static CopyName Make(string machineName, int pid, uint suffix)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pid);
        var machine = new string([.. machineName.ToLowerInvariant()
            .Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '_').Take(40)]);
        machine = machine.Length == 0 ? "_" : machine;
        return new CopyName(string.Create(CultureInfo.InvariantCulture, $"dbchange_{machine}_{pid}_{suffix:x8}"), machine, pid);
    }

    /// <summary>Ordinally.</summary>
    public int CompareTo(CopyName other) => string.CompareOrdinal(_text, other._text);

    public override string ToString() =>
        _text ?? throw new InvalidOperationException("default(CopyName) is not a name; make one with CopyName.Of or CopyName.Make.");
}

/// <summary>
/// A git ref as ref: names one: a branch, a tag, a commit or any revision git reads, not empty, not led by '-', which git would read as
/// an option, and holding no control character. Whether the ref names a commit is git's to say (io/Git). default(GitRef) is not a ref.
/// </summary>
public readonly record struct GitRef : IComparable<GitRef>
{
    private readonly string? _text;

    private GitRef(string text) => _text = text;

    /// <summary>The ref <paramref name="text"/> gives, or ref.malformed led by <paramref name="subject"/>.</summary>
    public static Result<GitRef> Of(string subject, string? text) =>
        text is { Length: > 0 } && !text.StartsWith('-') && !text.Any(char.IsControl) ? new GitRef(text)
        : new Error("ref.malformed",
            subject + " is no git ref: a ref is not empty, does not begin with '-', and holds no control character.",
            "Name a branch, a tag or a commit, such as main or v2.9.0.");

    /// <summary>Ordinally.</summary>
    public int CompareTo(GitRef other) => string.CompareOrdinal(_text, other._text);

    public override string ToString() =>
        _text ?? throw new InvalidOperationException("default(GitRef) is not a ref; make one with GitRef.Of.");
}
