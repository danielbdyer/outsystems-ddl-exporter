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
/// and a .publish.xml, through DacFx's own DacProfile, into its deploy options and SQLCMD values alone, refusing a password in it
/// and removing its target before anything keeps it. A refusal names the key or the file and quotes no value; DacFx's own
/// message, which can quote one, is withheld.
/// </summary>
public static class Profiles
{
    public const string Posture = "estate/posture.json";

    private static readonly string[] Keys = ["classification", "confirmedBy", "confirmedOn", "cohorts", "connection", "profile", "sqlcmd", "metamodel"];

    /// <summary>A password set in a connection string, however spelled or spaced.</summary>
    private static readonly Regex Password = new(@"(?:password|pwd)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>A key a message may name as it stands; any other is named by its place among its siblings.</summary>
    private static readonly Regex Nameable = new(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);

    /// <summary>The environments estate/posture.json names under the estate's root, in name order.</summary>
    public static Result<Seq<NamedEnvironment>> Environments(string estateRoot)
    {
        try
        {
            using var posture = JsonDocument.Parse(File.ReadAllText(Path.Combine(estateRoot, "estate", "posture.json")), new JsonDocumentOptions { AllowDuplicateProperties = false });
            var root = posture.RootElement;
            return Literal(root, "") is { } at
                ? new Refusal("posture.literal-connection", Posture + " holds a literal connection string at " + at + ".", "Move it into an environment variable or a file outside git, and write env:NAME or file:path at " + at + ".")
                : Unknown(root, "", ["environments"]) ?? (root.TryGetProperty("environments", out var environments) && environments.ValueKind == JsonValueKind.Object
                    ? All(environments.EnumerateObject().Select((e, i) => ReadEnvironment(e.Name, e.Value, Place("environments", e.Name, i)))).Map(read => Seq.Of(read))
                    : Malformed("environments", "an object of each environment by its name"));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Refusal("posture.missing", "No " + Posture + " under " + estateRoot + ".", "Commit " + Posture + " naming each environment's connection reference and publish profile.");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            var why = e is JsonException { LineNumber: { } line, BytePositionInLine: { } at } ? string.Create(CultureInfo.InvariantCulture, $"is not JSON at line {line + 1}, byte {at + 1}")
                : e is JsonException ? "gives one key twice in an object" : "cannot be opened";
            return new Refusal("posture.unreadable", Posture + " " + why + ".", "Correct " + Posture + " at the place this names; an object gives each key once.");
        }
    }

    /// <summary>The pipeline's publish profile at <paramref name="path"/>, as Strict.</summary>
    public static Result<Strict> Load(string path) => Load(path, "The profile " + path, path);

    /// <summary>
    /// A named environment's publish profile, from the estate's root, its refusals naming the environment. Its own SQLCMD values
    /// are not merged here: WP 1.4 resolves their references where it connects and sets them over the profile's in the options.
    /// </summary>
    public static Result<Strict> Of(NamedEnvironment environment, string estateRoot) =>
        Load(Path.GetFullPath(Path.Combine(estateRoot, environment.ProfilePath)), "env:" + environment.Name + "'s profile " + environment.ProfilePath, environment.ProfilePath);

    /// <summary>A profile at <paramref name="path"/>, its refusals led by <paramref name="owner"/> and naming the file as <paramref name="file"/>.</summary>
    private static Result<Strict> Load(string path, string owner, string file)
    {
        XDocument profile;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            profile = XDocument.Load(reader);
            if (Password.IsMatch(Encoding.UTF8.GetString(bytes)) || Password.IsMatch(profile.ToString()))   // as written, and as its character references read
            {
                return new Refusal("profile.password", owner + " holds a password in a connection string; a profile gives deploy options and SQLCMD values alone.",
                    "Delete the connection string from " + file + ", and name the connection in " + Posture + " as env:NAME or file:path.");
            }
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Refusal("profile.missing", "No publish profile at " + path + ".", "Name the pipeline's .publish.xml by its path from the estate's root, as the profile of " + Posture + " does.");
        }
        catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException)
        {
            var why = e is XmlException x ? string.Create(CultureInfo.InvariantCulture, $"is not XML at line {x.LineNumber}, position {x.LinePosition}") : "cannot be opened";
            return new Refusal("profile.unreadable", owner + " " + why + ".", "Correct the file at the place this names, or save the profile again from Visual Studio.");
        }

        profile.Descendants().Where(e => e.Name.LocalName is "TargetConnectionString" or "TargetDatabaseName").Remove();
        using var kept = new MemoryStream();
        profile.Save(kept);
        try
        {
            var options = DacProfile.Load(new MemoryStream(kept.ToArray(), writable: false)).DeployOptions;
            return !options.BlockOnPossibleDataLoss
                ? new Refusal("profile.guard-off", owner + " sets BlockOnPossibleDataLoss to False; Strict is the pipeline's profile with the guard on.", "Set BlockOnPossibleDataLoss to True in " + file + "; only a copy publishes with the guard off, as Permissive.")
                : All(options.SqlCommandVariableValues.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => SqlCmdVariable.Of(owner, v.Key, v.Value ?? ""))).Map(values => new Strict(path, kept.ToArray(), Seq.Of(values)));
        }
        catch (Exception e) when (e is DacServicesException or ArgumentException or FormatException or InvalidOperationException or XmlException)
        {
            return new Refusal("profile.unreadable", owner + " is a file DacFx does not read as a publish profile; its message is withheld, since it can quote the file's values.", "Compare it with a profile Visual Studio saves (Publish, then Save Profile As) and correct it.");
        }
    }

    private static Result<NamedEnvironment> ReadEnvironment(string name, JsonElement environment, string at)
    {
        string? Given(string key) => environment.TryGetProperty(key, out var value) ? value.GetString() : null;
        if ((Unknown(environment, at, Keys) ?? environment.EnumerateObject().Select(p => Kind(p, at)).FirstOrDefault(wrong => wrong is not null)) is { } refused)
        {
            return refused;
        }

        if ((Given("connection") is null ? "connection" : Given("profile") is null ? "profile" : null) is { } missing)
        {
            return new Refusal("posture.malformed", Where(at) + " has no " + missing + ".", "Give " + at + " its " + missing + "; each environment takes a connection and a profile.");
        }

        var subject = Where(at);
        List<string> cohorts = environment.TryGetProperty("cohorts", out var names) ? [.. names.EnumerateArray().Select(c => c.GetString()!)] : [];
        var confirmation = Given("confirmedBy") is null && Given("confirmedOn") is null ? Result.Ok<Confirmation?>(null) : Confirmation.Of(subject, Given("confirmedBy"), Given("confirmedOn")).Map(c => (Confirmation?)c);
        var metamodel = Given("metamodel") is { } reference ? SecretReference.Of(Where(at + ".metamodel"), reference).Map(m => (SecretReference?)m) : Result.Ok<SecretReference?>(null);
        var sqlCmd = environment.TryGetProperty("sqlcmd", out var values) ? All(values.EnumerateObject().Select((v, i) => Variable(v.Name, v.Value, Place(at + ".sqlcmd", v.Name, i)))) : new List<SqlCmdVariable>();
        return confirmation.Bind(confirmed => Classification.Of(subject, Given("classification"), confirmed)).Bind(classification =>
            SecretReference.Of(Where(at + ".connection"), Given("connection")).Bind(connection => metamodel.Bind(meta => sqlCmd.Bind(variables =>
                NamedEnvironment.Of(subject, name, classification, cohorts, connection, Given("profile")!, variables, meta)))));
    }

    /// <summary>The refusal of an environment's key whose value is of another JSON kind than the key takes, or null.</summary>
    private static Refusal? Kind(JsonProperty key, string at) => (key.Name, key.Value.ValueKind) switch
    {
        ("cohorts", JsonValueKind.Array) when key.Value.EnumerateArray().All(c => c.ValueKind == JsonValueKind.String) => null,
        ("cohorts", _) => Malformed(at + ".cohorts", "an array of cohort names"),
        ("sqlcmd", JsonValueKind.Object) or (not ("cohorts" or "sqlcmd"), JsonValueKind.String) => null,
        ("sqlcmd", _) => Malformed(at + ".sqlcmd", "an object of SQLCMD values by variable name"),
        _ => Malformed(at + "." + key.Name, "a string"),
    };

    /// <summary>A SQLCMD value: a string is a reference, and an object a literal, taken only when marked "sensitive": false.</summary>
    private static Result<SqlCmdVariable> Variable(string name, JsonElement value, string at) => value.ValueKind switch
    {
        JsonValueKind.String when value.GetString() is { } text && (text.StartsWith("env:", StringComparison.Ordinal) || text.StartsWith("file:", StringComparison.Ordinal)) =>
            SecretReference.Of(Where(at), text).Bind(reference => SqlCmdVariable.Of(Where(at), name, reference)),
        JsonValueKind.Object when Unknown(value, at, ["literal", "sensitive"]) is { } unknown => unknown,
        JsonValueKind.Object when !value.TryGetProperty("literal", out var literal) || literal.ValueKind != JsonValueKind.String => Malformed(at + ".literal", "a string"),
        JsonValueKind.Object when value.TryGetProperty("sensitive", out var sensitive) && sensitive.ValueKind == JsonValueKind.False => SqlCmdVariable.Of(Where(at), name, value.GetProperty("literal").GetString()!),
        JsonValueKind.Object or JsonValueKind.String => new Refusal("posture.unmarked-literal", Where(at) + " gives a literal not marked \"sensitive\": false.",
            "Write it as env:NAME or file:path, or, where the value is safe to commit, as { \"literal\": its text, \"sensitive\": false }."),
        _ => Malformed(at, "env:NAME, file:path or { \"literal\": its text, \"sensitive\": false }"),
    };

    /// <summary>Where the first key or string of the document that is a literal connection string sits, or null.</summary>
    private static string? Literal(JsonElement element, string at) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Select((p, i) => IsConnection(p.Name) ? Place(at, p.Name, i) : Literal(p.Value, Place(at, p.Name, i))).FirstOrDefault(found => found is not null),
        JsonValueKind.Array => element.EnumerateArray().Select((item, i) => Literal(item, string.Create(CultureInfo.InvariantCulture, $"{at}[{i}]"))).FirstOrDefault(found => found is not null),
        JsonValueKind.String => IsConnection(element.GetString()!) ? at : null,
        _ => null,
    };

    /// <summary>Whether a text is a literal connection string: it sets a password, or SqlClient's own grammar reads one of its keywords from it (Server, Database, User ID, a synonym).</summary>
    private static bool IsConnection(string text)
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

    /// <summary>The refusal of a value that is no object, or of an object's first key outside <paramref name="known"/>; null when neither.</summary>
    private static Refusal? Unknown(JsonElement element, string at, string[] known) => element.ValueKind != JsonValueKind.Object ? Malformed(at, "a JSON object")
        : element.EnumerateObject().Select((p, i) => known.Contains(p.Name) ? null
            : new Refusal("posture.unknown-key", Posture + " has an unknown key at " + Place(at, p.Name, i) + ".", "Remove it, or spell it as one of: " + string.Join(", ", known) + ".")).FirstOrDefault(r => r is not null);

    private static Refusal Malformed(string at, string takes) => new Refusal("posture.malformed", Where(at) + " is not " + takes + ".", "Write " + Where(at) + " as " + takes + ".");

    private static string Where(string at) => at.Length == 0 ? Posture : at + " in " + Posture;

    /// <summary>A key's place: its path, then its name where a message may name it as it stands, else its place among its siblings.</summary>
    private static string Place(string at, string key, int index) =>
        (at.Length == 0 ? "" : at + ".") + (Nameable.IsMatch(key) ? key : string.Create(CultureInfo.InvariantCulture, $"#{index + 1}"));

    /// <summary>Each value in order, or the first refusal among the results.</summary>
    private static Result<List<T>> All<T>(IEnumerable<Result<T>> results) =>
        results.Aggregate(Result.Ok(new List<T>()), (all, next) => all.Bind(made => next.Map(value => (List<T>)[.. made, value])));

    /// <summary>
    /// A publish profile as the engine uses it (§1 fact 10): DacFx's deploy options and the profile's SQLCMD values, nothing else.
    /// It keeps the profile's XML, its target removed, and reads a fresh copy of the options on each call, so a change a caller
    /// makes to one copy reaches no other. Strict and Permissive are its two forms.
    /// </summary>
    public abstract class PublishProfile
    {
        private readonly byte[] _profile;

        private protected PublishProfile(string source, byte[] profile, Seq<SqlCmdVariable> sqlCmd) => (Source, _profile, SqlCmd) = (source, profile, sqlCmd);

        private protected PublishProfile(PublishProfile from)
            : this(from.Source, from._profile, from.SqlCmd)
        {
        }

        /// <summary>The file it was loaded from.</summary>
        public string Source { get; }

        /// <summary>The profile's own SQLCMD values: literals, none under a name shaped like a credential.</summary>
        public Seq<SqlCmdVariable> SqlCmd { get; }

        /// <summary>A fresh copy of the options, for io's calls into DacFx that plan and publish (WP 1.4); internal, so nothing outside io holds one.</summary>
        internal virtual DacDeployOptions Options() => DacProfile.Load(new MemoryStream(_profile, writable: false)).DeployOptions;
    }

    /// <summary>The pipeline's profile as loaded, the guard on: every plan uses it, and every publish unless a copy asks for Permissive.</summary>
    public sealed class Strict : PublishProfile
    {
        internal Strict(string source, byte[] profile, Seq<SqlCmdVariable> sqlCmd)
            : base(source, profile, sqlCmd)
        {
        }

        public override string ToString() => "Strict: " + Source;
    }

    /// <summary>
    /// Strict with BlockOnPossibleDataLoss off and nothing else changed (§1 fact 10), for a copy alone (§2.1 rule 3), to see what the
    /// guard would have stopped. Nothing public makes one. For WP 1.4, which creates Copy: make Copy the one caller of
    /// <see cref="Of"/>, from a member of Copy or one taking a Copy (a Copy.Publish of a Strict, permissively, say), and call it
    /// nowhere else in io. Of stays internal and takes a Strict, so no caller of Load, Of or Strict changes; ProfilesTests' "nothing
    /// public makes a Permissive profile" already admits such a member and refuses any other; SentinelTests calls Of until 1.4
    /// routes it through Copy.Publish.
    /// </summary>
    public sealed class Permissive : PublishProfile
    {
        private Permissive(Strict strict)
            : base(strict)
        {
        }

        public override string ToString() => "Permissive: " + Source;

        internal static Permissive Of(Strict strict) => new(strict);

        internal override DacDeployOptions Options()
        {
            var options = base.Options();
            options.BlockOnPossibleDataLoss = false;
            return options;
        }
    }
}
