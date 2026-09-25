using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

namespace Estate.Io;

/// <summary>
/// The estate's posture and the pipeline's publish profile, read as data (V3_MILESTONES.md WP 1.5, §1 fact 10, §4 row 14):
/// estate/posture.json into named environments, refusing a literal connection string anywhere in it and a key it does not know;
/// and a .publish.xml, through DacFx's own DacProfile, into its deploy options and SQLCMD values alone, its target removed first,
/// refusing a password anywhere in it and a SQLCMD value that is a connection string. A refusal names the key or the file and
/// quotes no value; DacFx's own message, which can quote one, is withheld.
/// </summary>
public static class Profiles
{
    public const string Posture = "estate/posture.json";

    private static readonly string[] Keys = ["classification", "confirmedBy", "confirmedOn", "cohorts", "connection", "profile", "sqlcmd", "metamodel"];

    /// <summary>A password set in a connection string, however spelled or spaced.</summary>
    private static readonly Regex Password = new(@"(?:password|pwd)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>A key a message may name as it stands; any other is named by its place among its siblings.</summary>
    private static readonly Regex Nameable = new(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// How a profile is kept, and so fingerprinted: UTF-8 with no byte-order mark, LF line ends, two-space indent. XDocument.Save's
    /// defaults follow Environment.NewLine and write a byte-order mark, so one profile kept on Windows and on Linux hashed apart.
    /// </summary>
    private static readonly XmlWriterSettings Kept = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Indent = true, NewLineChars = "\n", NewLineHandling = NewLineHandling.Replace,
    };

    /// <summary>The estate's root: the nearest directory at or above <paramref name="workingDirectory"/> holding estate/posture.json, else the working directory.</summary>
    public static string Root(string workingDirectory)
    {
        for (var directory = new DirectoryInfo(workingDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Posture)))
            {
                return directory.FullName;
            }
        }

        return Path.GetFullPath(workingDirectory);
    }

    /// <summary>The environments estate/posture.json names under the estate's root, in name order.</summary>
    public static Result<Seq<NamedEnvironment>> Environments(string estateRoot)
    {
        try
        {
            using var posture = JsonDocument.Parse(File.ReadAllText(Path.Combine(estateRoot, Posture)), new JsonDocumentOptions { AllowDuplicateProperties = false });
            var root = posture.RootElement;
            return Literal(root, "") is { } at ? new Refusal("posture.literal-connection", Posture + " holds a literal connection string at " + at + ".",
                    "Move it into an environment variable or a file outside git, and write env:NAME or file:path at " + at + ".")
                : (Unknown(root, "", ["environments", "substrate"]) ?? Missing(root, "", "environments"))
                    ?? All(root.GetProperty("environments").EnumerateObject().Select((e, i) => EnvironmentAt(e.Name, e.Value, Place("environments", e.Name, i))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            var why = e is JsonException { LineNumber: { } line, BytePositionInLine: { } at }
                ? string.Create(CultureInfo.InvariantCulture, $"is not JSON at line {line + 1}, byte {at + 1}")
                : e is JsonException ? "gives one key twice in an object" : "cannot be opened";
            return e is FileNotFoundException or DirectoryNotFoundException ? new Refusal("posture.missing", "No " + Posture + " under " + estateRoot + ".",
                    "Commit " + Posture + " naming each environment's connection reference and publish profile.")
                : new Refusal("posture.unreadable", Posture + " " + why + ".", "Correct " + Posture + " at the place this names; an object gives each key once.");
        }
    }

    /// <summary>
    /// The publish profile at <paramref name="path"/> as Strict: DacFx's reading of it, its target removed first. A password is sought
    /// as the file is written and as DacFx reads each value, a comment inside it dropped and a character reference read. A refusal
    /// names the file and leads with <paramref name="subject"/>: "The profile" and the path, unless the caller says whose profile it is.
    /// </summary>
    public static Result<PublishProfile.Strict> Load(string path, string? subject = null)
    {
        subject ??= "The profile " + path;
        XDocument profile;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            profile = XDocument.Load(reader);
            var read = profile.Descendants().SelectMany(e => e.Attributes().Select(a => a.Value).Append(string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value))));
            if (read.Prepend(Encoding.UTF8.GetString(bytes)).Any(Password.IsMatch))
            {
                return new Refusal("profile.password", subject + " holds a password in a connection string; a profile gives deploy options and SQLCMD values alone.",
                    "Delete the connection string from " + path + ", and name the connection in " + Posture + " as env:NAME or file:path.");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return e is FileNotFoundException or DirectoryNotFoundException ? new Refusal("profile.missing", "No publish profile at " + path + ".",
                    "Name the pipeline's .publish.xml by its path from the estate's root, as the profile of " + Posture + " does.")
                : new Refusal("profile.unreadable", subject + (e is XmlException x
                    ? string.Create(CultureInfo.InvariantCulture, $" is not XML at line {x.LineNumber}, position {x.LinePosition}.") : " cannot be opened."),
                    "Correct the file at the place this names, or save the profile again from Visual Studio.");
        }

        profile.Descendants().Where(e => e.Name.LocalName is "TargetConnectionString" or "TargetDatabaseName").Remove();
        using var kept = new MemoryStream();
        using (var writer = XmlWriter.Create(kept, Kept))
        {
            profile.Save(writer);
        }

        try
        {
            var options = DacProfile.Load(new MemoryStream(kept.ToArray(), writable: false)).DeployOptions;
            return !options.BlockOnPossibleDataLoss ? new Refusal("profile.guard-off",
                    subject + " sets BlockOnPossibleDataLoss to False; Strict is the pipeline's profile with the guard on.",
                    "Set BlockOnPossibleDataLoss to True in " + path + "; only a copy publishes with the guard off, as Permissive.")
                : All(options.SqlCommandVariableValues.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => ProfileValue(subject, path, v.Key, v.Value ?? "")))
                    .Map(values => new PublishProfile.Strict(path, kept.ToArray(), values));
        }
        catch (Exception e) when (e is DacServicesException or ArgumentException or FormatException or InvalidOperationException or XmlException)
        {
            return new Refusal("profile.unreadable", subject + " is a file DacFx does not read as a publish profile; its message is withheld, since it can quote a value.",
                "Compare it with a profile Visual Studio saves (Publish, then Save Profile As) and correct it.");
        }
    }

    /// <summary>A named environment's profile, its refusals led by the environment; io/SqlServer.Plan sets the environment's own SQLCMD values over the profile's.</summary>
    public static Result<PublishProfile.Strict> Of(NamedEnvironment environment, string estateRoot) =>
        Load(Path.GetFullPath(Path.Combine(estateRoot, environment.ProfilePath)), "env:" + environment.Name + "'s profile " + environment.ProfilePath);

    /// <summary>A SQLCMD value a profile gives: a literal, refused under a name shaped like a credential or when it is a connection string.</summary>
    private static Result<SqlCmdVariable> ProfileValue(string subject, string path, string name, string value) =>
        SqlCmdVariable.Of(subject, name, value).Bind(literal => IsConnection(value) ? new Refusal("profile.literal-connection",
            subject + " gives $(" + name + ") a literal connection string.",
            "Give $(" + name + ") as env:NAME or file:path in the environment's sqlcmd in " + Posture + ", and delete its value from " + path + ".") : Result.Ok(literal));

    private static Result<NamedEnvironment> EnvironmentAt(string name, JsonElement entry, string at)
    {
        if ((Unknown(entry, at, Keys) ?? Missing(entry, at, "connection", "profile")) is { } refused)
        {
            return refused;
        }

        var subject = Where(at);
        string? Given(string key) => entry.TryGetProperty(key, out var value) ? value.GetString() : null;
        string[] cohorts = entry.TryGetProperty("cohorts", out var names) ? [.. names.EnumerateArray().Select(c => c.GetString()!)] : [];
        var confirmation = Given("confirmedBy") is null && Given("confirmedOn") is null ? Result.Ok<Confirmation?>(null)
            : Confirmation.Of(subject, Given("confirmedBy"), Given("confirmedOn")).Map(c => (Confirmation?)c);
        var metamodel = Given("metamodel") is { } reference
            ? SecretReference.Of(Where(at + ".metamodel"), reference).Map(m => (SecretReference?)m) : Result.Ok<SecretReference?>(null);
        var sqlCmd = entry.TryGetProperty("sqlcmd", out var values)
            ? All(values.EnumerateObject().Select((v, i) => PostureValue(v.Name, v.Value, Place(at + ".sqlcmd", v.Name, i)))) : default(Seq<SqlCmdVariable>);
        return confirmation.Bind(confirmed => Classification.Of(subject, Given("classification"), confirmed)).Bind(classification =>
            SecretReference.Of(Where(at + ".connection"), Given("connection")).Bind(connection => metamodel.Bind(meta => sqlCmd.Bind(variables =>
                NamedEnvironment.Of(subject, name, classification, cohorts, connection, Given("profile")!, variables, meta)))));
    }

    /// <summary>A SQLCMD value in the posture: a string is a reference, and an object a literal, taken only when marked "sensitive": false.</summary>
    private static Result<SqlCmdVariable> PostureValue(string name, JsonElement value, string at) => value.ValueKind switch
    {
        JsonValueKind.String when Text(value) is { } text && (text.StartsWith("env:", StringComparison.Ordinal) || text.StartsWith("file:", StringComparison.Ordinal)) =>
            SecretReference.Of(Where(at), text).Bind(reference => SqlCmdVariable.Of(Where(at), name, reference)),
        JsonValueKind.Object when (Unknown(value, at, ["literal", "sensitive"]) ?? Missing(value, at, "literal")) is { } refused => refused,
        JsonValueKind.Object when value.TryGetProperty("sensitive", out var sensitive) && sensitive.ValueKind == JsonValueKind.False =>
            SqlCmdVariable.Of(Where(at), name, Text(value.GetProperty("literal"))!),
        JsonValueKind.Object or JsonValueKind.String => new Refusal("posture.unmarked-literal", Where(at) + " gives a literal not marked \"sensitive\": false.",
            "Write it as env:NAME or file:path, or, where the value is safe to commit, as { \"literal\": its text, \"sensitive\": false }."),
        _ => Malformed(at, "env:NAME, file:path or { \"literal\": its text, \"sensitive\": false }"),
    };

    /// <summary>Where the first key or string of the document that is a literal connection string sits, or null.</summary>
    private static string? Literal(JsonElement element, string at) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Select((p, i) => IsConnection(p.Name) ? Place(at, p.Name, i) : Literal(p.Value, Place(at, p.Name, i)))
            .FirstOrDefault(found => found is not null),
        JsonValueKind.Array => element.EnumerateArray().Select((item, i) => Literal(item, string.Create(CultureInfo.InvariantCulture, $"{at}[{i}]")))
            .FirstOrDefault(found => found is not null),
        JsonValueKind.String => IsConnection(element.GetString()!) ? at : null,
        _ => null,
    };

    /// <summary>Whether a text is a literal connection string: it sets a password, or SqlClient's grammar reads one of its keywords from it (Server, User ID). io/SqlServer's target grammar asks it of an argument.</summary>
    internal static bool IsConnection(string text)
    {
        try
        {
            return Password.IsMatch(text) || new DbConnectionStringBuilder { ConnectionString = text }.Keys.Cast<string>().Any(new SqlConnectionStringBuilder().ContainsKey);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The first refusal of an object in the posture: none at all, a key outside <paramref name="known"/>, or a value of another kind than its key takes.</summary>
    private static Refusal? Unknown(JsonElement element, string at, string[] known) => element.ValueKind != JsonValueKind.Object ? Malformed(at, "a JSON object")
        : element.EnumerateObject().Select((p, i) => known.Contains(p.Name) ? Kind(p, Place(at, p.Name, i)) : new Refusal("posture.unknown-key",
            Posture + " has an unknown key at " + Place(at, p.Name, i) + ".", "Remove it, or spell it as one of: " + string.Join(", ", known) + "."))
            .FirstOrDefault(r => r is not null);

    /// <summary>The refusal of a value of another kind than its key takes, or null: a string, unless the key takes another.</summary>
    private static Refusal? Kind(JsonProperty key, string at) => (key.Name, key.Value.ValueKind) switch
    {
        ("cohorts", JsonValueKind.Array) when key.Value.EnumerateArray().All(c => c.ValueKind == JsonValueKind.String) => null,
        ("cohorts", _) => Malformed(at, "an array of cohort names"),
        ("environments" or "sqlcmd", JsonValueKind.Object) or ("sensitive", _) => null,
        ("environments", _) => Malformed(at, "an object of each environment by its name"),
        ("sqlcmd", _) => Malformed(at, "an object of SQLCMD values by variable name"),
        ("substrate", _) => Text(key.Value) is "docker" or "localdb" ? null : Malformed(at, "docker or localdb"),
        _ => key.Value.ValueKind == JsonValueKind.String ? null : Malformed(at, "a string"),
    };

    /// <summary>The refusal of an object that lacks a key it must give, or null.</summary>
    private static Refusal? Missing(JsonElement element, string at, params string[] keys) => keys.Where(key => !element.TryGetProperty(key, out _))
        .Select(key => new Refusal("posture.malformed", Where(at) + " has no " + key + ".", "Give " + Where(at) + " its " + key + ".")).FirstOrDefault();

    private static Refusal Malformed(string at, string takes) => new("posture.malformed", Where(at) + " is not " + takes + ".", "Write " + Where(at) + " as " + takes + ".");

    private static string Where(string at) => at.Length == 0 ? Posture : at + " in " + Posture;

    /// <summary>A key's place: its path, then its name where a message may name it as it stands, else its place among its siblings.</summary>
    private static string Place(string at, string key, int index) =>
        (at.Length == 0 ? "" : at + ".") + (Nameable.IsMatch(key) ? key : string.Create(CultureInfo.InvariantCulture, $"#{index + 1}"));

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Each value, sorted, or the first refusal among the results.</summary>
    private static Result<Seq<T>> All<T>(IEnumerable<Result<T>> results) where T : IComparable<T> =>
        results.Aggregate(Result.Ok(new List<T>()), (all, next) => all.Bind(made => next.Map(value => (List<T>)[.. made, value]))).Map(made => Seq.Of(made));
}

/// <summary>
/// A publish profile as the engine uses it (§1 fact 10): DacFx's deploy options and the profile's SQLCMD values, nothing else. It keeps
/// the profile's XML, its target removed, and reads a fresh copy of the options on each call, so a change a caller makes to one copy
/// reaches no other. Its two cases are closed: Strict, the pipeline's profile as loaded, and Permissive, the same with the guard off.
/// </summary>
public abstract class PublishProfile
{
    private readonly byte[] _profile;

    private PublishProfile(string source, byte[] profile, Seq<SqlCmdVariable> sqlCmd) => (Source, _profile, SqlCmd) = (source, profile, sqlCmd);

    /// <summary>The file it was loaded from.</summary>
    public string Source { get; }

    /// <summary>The profile's own SQLCMD values: literals, none under a name shaped like a credential and none a connection string.</summary>
    public Seq<SqlCmdVariable> SqlCmd { get; }

    /// <summary>A receipt's profile input: the fingerprint of the profile as kept, its target removed, in UTF-8 with LF line ends and no byte-order mark on every operating system.</summary>
    public Fingerprint Fingerprint => Fingerprint.Of(_profile);

    public override string ToString() => (this is Strict ? "Strict: " : "Permissive: ") + Source;

    /// <summary>A fresh copy of the options, the guard on for Strict and off for Permissive, for io/SqlServer's calls into DacFx that plan and publish.</summary>
    internal DacDeployOptions Options()
    {
        var options = DacProfile.Load(new MemoryStream(_profile, writable: false)).DeployOptions;
        options.BlockOnPossibleDataLoss = this is Strict;
        return options;
    }

    /// <summary>The pipeline's profile as loaded, the guard on: every plan uses it, and every publish unless a copy asks for Permissive.</summary>
    public sealed class Strict : PublishProfile
    {
        internal Strict(string source, byte[] profile, Seq<SqlCmdVariable> sqlCmd)
            : base(source, profile, sqlCmd)
        {
        }
    }

    /// <summary>
    /// Strict with BlockOnPossibleDataLoss off and nothing else changed (§1 fact 10), for a copy alone (§2.1 rule 3), to see what the
    /// guard would have stopped. SqlServer.Copy.Permissive is Of's one caller, so a Permissive profile exists only for a copy;
    /// ProfilesTests' "nothing but a Copy makes a Permissive profile" fails on a call from anywhere else in io.
    /// </summary>
    public sealed class Permissive : PublishProfile
    {
        private Permissive(Strict strict)
            : base(strict.Source, strict._profile, strict.SqlCmd)
        {
        }

        internal static Permissive Of(Strict strict) => new(strict);
    }
}
