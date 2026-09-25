using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Estate.Kernel;

namespace Estate.Io;

/// <summary>
/// The estate's posture, read as data (V3_MILESTONES.md WP 1.5, §4 row 14): estate/posture.json into named environments, refusing a
/// literal connection string anywhere in it and a key it does not know. An error names the key and quotes no value. A publish profile is
/// io/PublishProfiles' to read.
/// </summary>
public static class Posture
{
    /// <summary>The posture's path from the estate's root, as messages name it.</summary>
    public const string Json = "estate/posture.json";

    private static readonly string[] Keys = ["host", "classification", "confirmedBy", "confirmedOn", "readers", "connection", "profile", "sqlcmd", "metamodel"];

    /// <summary>A key a message may name as it stands; any other is named by its place among its siblings.</summary>
    private static readonly Regex Nameable = new(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);

    /// <summary>The estate's root: the nearest directory at or above <paramref name="workingDirectory"/> holding estate/posture.json, else the working directory.</summary>
    public static string Root(string workingDirectory)
    {
        for (var directory = new DirectoryInfo(workingDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Json)))
            {
                return directory.FullName;
            }
        }

        return Path.GetFullPath(workingDirectory);
    }

    /// <summary>
    /// estate/posture.json under the estate's root, read once for a verb: the environments it names, in name order, each with the host
    /// its SQL Server runs on, and the scratch server it prefers.
    /// </summary>
    public static Result<Environments> Environments(string estateRoot)
    {
        try
        {
            using var posture = JsonDocument.Parse(File.ReadAllText(Path.Combine(estateRoot, Json)), new JsonDocumentOptions { AllowDuplicateProperties = false });
            var root = posture.RootElement;
            return Literal(root, "") is { } at ? new Error("posture.literal-connection", Json + " holds a literal connection string at " + at + ".",
                    "Move it into an environment variable or a file outside git, and write env:NAME or file:path at " + at + ".")
                : (Unknown(root, "", ["environments", "scratchServer"]) ?? Missing(root, "", "environments"))
                    ?? Result.All(root.GetProperty("environments").EnumerateObject().Select((e, i) => EnvironmentAt(e.Name, e.Value, Place("environments", e.Name, i))))
                        .Bind(environments => (root.TryGetProperty("scratchServer", out var kind)
                                ? ScratchServerKind.Of(Where("scratchServer"), kind.GetString()).Map(k => (ScratchServerKind?)k)
                                : Result.Ok<ScratchServerKind?>(null))
                            .Bind(scratchServer => Kernel.Environments.Of(Json, environments, scratchServer)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            var why = e is JsonException { LineNumber: { } line, BytePositionInLine: { } at }
                ? string.Create(CultureInfo.InvariantCulture, $"is not JSON at line {line + 1}, byte {at + 1}")
                : e is JsonException ? "gives one key twice in an object" : "cannot be opened";
            return e is FileNotFoundException or DirectoryNotFoundException ? new Error("posture.missing", "No " + Json + " under " + estateRoot + ".",
                    "Commit " + Json + " naming each environment's connection reference and publish profile.")
                : new Error("posture.unreadable", Json + " " + why + ".", "Correct " + Json + " at the place this names; an object gives each key once.");
        }
    }

    private static Result<NamedEnvironment> EnvironmentAt(string name, JsonElement entry, string at)
    {
        if ((Unknown(entry, at, Keys) ?? Missing(entry, at, "connection", "profile")) is { } error)
        {
            return error;
        }

        var subject = Where(at);
        string? Given(string key) => entry.TryGetProperty(key, out var value) ? value.GetString() : null;
        string[] readers = entry.TryGetProperty("readers", out var names) ? [.. names.EnumerateArray().Select(g => g.GetString()!)] : [];
        var confirmation = Given("confirmedBy") is null && Given("confirmedOn") is null ? Result.Ok<Confirmation?>(null)
            : Confirmation.Of(subject, Given("confirmedBy"), Given("confirmedOn")).Map(c => (Confirmation?)c);
        var metamodel = Given("metamodel") is { } reference
            ? SecretReference.Of(Where(at + ".metamodel"), reference).Map(m => (SecretReference?)m) : Result.Ok<SecretReference?>(null);
        var sqlCmd = entry.TryGetProperty("sqlcmd", out var values)
            ? Result.All(values.EnumerateObject().Select((v, i) => PostureValue(v.Name, v.Value, Place(at + ".sqlcmd", v.Name, i)))).Map(variables => SortedArray.Of(variables))
            : default(SortedArray<SqlCmdVariable>);
        return confirmation.Bind(confirmed => Classification.Of(subject, Given("classification"), confirmed)).Bind(classification =>
            SecretReference.Of(Where(at + ".connection"), Given("connection")).Bind(connection => metamodel.Bind(meta => sqlCmd.Bind(variables =>
                EnvironmentName.Of(subject, name).Bind(environment => HostOf(entry, at).Bind(host => PublishProfilePath.Of(subject, Given("profile")).Bind(profile =>
                    NamedEnvironment.Of(subject, environment, host, classification, readers, connection, profile, variables, meta))))))));
    }

    /// <summary>
    /// The host an environment's SQL Server runs on (DECISIONS.md, 2026-09-25): each environment names one, so R15 compares every
    /// environment with the scratch server, its reference resolving on this machine or not; posture.host when the key is absent.
    /// </summary>
    private static Result<Host> HostOf(JsonElement entry, string at) => entry.TryGetProperty("host", out var host)
        ? Host.Of(Where(at + ".host"), host.GetString())
        : new Error("posture.host", Where(at) + " names no host; each environment names the host its SQL Server runs on, so estate makes no copy on it.",
            "Give " + Where(at) + " its host, the server's name as its connection string spells it, such as dev-sql.corp.example.");

    /// <summary>A SQLCMD value in the posture: a string is a reference, and an object a literal, taken only when marked "sensitive": false.</summary>
    private static Result<SqlCmdVariable> PostureValue(string name, JsonElement value, string at) => value.ValueKind switch
    {
        JsonValueKind.String when Text(value) is { } text && (text.StartsWith("env:", StringComparison.Ordinal) || text.StartsWith("file:", StringComparison.Ordinal)) =>
            SecretReference.Of(Where(at), text).Bind(reference => SqlCmdVariable.Of(Where(at), name, reference)),
        JsonValueKind.Object when (Unknown(value, at, ["literal", "sensitive"]) ?? Missing(value, at, "literal")) is { } error => error,
        JsonValueKind.Object when value.TryGetProperty("sensitive", out var sensitive) && sensitive.ValueKind == JsonValueKind.False =>
            SqlCmdVariable.Of(Where(at), name, Text(value.GetProperty("literal"))!),
        JsonValueKind.Object or JsonValueKind.String => new Error("posture.unmarked-literal", Where(at) + " gives a literal not marked \"sensitive\": false.",
            "Write it as env:NAME or file:path, or, where the value is safe to commit, as { \"literal\": its text, \"sensitive\": false }."),
        _ => Malformed(at, "env:NAME, file:path or { \"literal\": its text, \"sensitive\": false }"),
    };

    /// <summary>Where the first key or string of the document that is a literal connection string sits, or null.</summary>
    private static string? Literal(JsonElement element, string at) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Select((p, i) => ConnectionString.IsConnection(p.Name) ? Place(at, p.Name, i) : Literal(p.Value, Place(at, p.Name, i)))
            .FirstOrDefault(found => found is not null),
        JsonValueKind.Array => element.EnumerateArray().Select((item, i) => Literal(item, string.Create(CultureInfo.InvariantCulture, $"{at}[{i}]")))
            .FirstOrDefault(found => found is not null),
        JsonValueKind.String => ConnectionString.IsConnection(element.GetString()!) ? at : null,
        _ => null,
    };

    /// <summary>The first error in an object of the posture: none at all, a key outside <paramref name="known"/>, or a value of another kind than its key takes.</summary>
    private static Error? Unknown(JsonElement element, string at, string[] known) => element.ValueKind != JsonValueKind.Object ? Malformed(at, "a JSON object")
        : element.EnumerateObject().Select((p, i) => known.Contains(p.Name) ? Kind(p, Place(at, p.Name, i)) : new Error("posture.unknown-key",
            Json + " has an unknown key at " + Place(at, p.Name, i) + ".", "Remove it, or spell it as one of: " + string.Join(", ", known) + "."))
            .FirstOrDefault(r => r is not null);

    /// <summary>The error of a value of another kind than its key takes, or null: a string, unless the key takes another.</summary>
    private static Error? Kind(JsonProperty key, string at) => (key.Name, key.Value.ValueKind) switch
    {
        ("readers", JsonValueKind.Array) when key.Value.EnumerateArray().All(g => g.ValueKind == JsonValueKind.String) => null,
        ("readers", _) => Malformed(at, "an array of group names"),
        ("environments" or "sqlcmd", JsonValueKind.Object) or ("sensitive", _) => null,
        ("environments", _) => Malformed(at, "an object of each environment by its name"),
        ("sqlcmd", _) => Malformed(at, "an object of SQLCMD values by variable name"),
        _ => key.Value.ValueKind == JsonValueKind.String ? null : Malformed(at, "a string"),
    };

    /// <summary>The error of an object that lacks a key it must give, or null.</summary>
    private static Error? Missing(JsonElement element, string at, params string[] keys) => keys.Where(key => !element.TryGetProperty(key, out _))
        .Select(key => new Error("posture.malformed", Where(at) + " has no " + key + ".", "Give " + Where(at) + " its " + key + ".")).FirstOrDefault();

    private static Error Malformed(string at, string takes) => new("posture.malformed", Where(at) + " is not " + takes + ".", "Write " + Where(at) + " as " + takes + ".");

    private static string Where(string at) => at.Length == 0 ? Json : at + " in " + Json;

    /// <summary>A key's place: its path, then its name where a message may name it as it stands, else its place among its siblings.</summary>
    private static string Place(string at, string key, int index) =>
        (at.Length == 0 ? "" : at + ".") + (Nameable.IsMatch(key) ? key : string.Create(CultureInfo.InvariantCulture, $"#{index + 1}"));

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
