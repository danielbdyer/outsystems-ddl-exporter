using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Kernel;

namespace Estate.Cli;

/// <summary>The renderers: the help and every answer, as Markdown or as JSON, and the JSON schemas generated from the contract.</summary>
public static class Render
{
    public const string Usage = "estate <verb> [arguments] [--json] | estate --help [--json] | estate --version";

    private const string SchemaId = "^estate\\.[a-z]+/[1-9][0-9]*$";

    public static JsonObject Help() => new()
    {
        ["schema"] = "estate.help/1",
        ["version"] = Contract.Version,
        ["milestone"] = Contract.M(Contract.Milestone),
        ["usage"] = Usage,
        ["verbs"] = Array(Contract.Verbs.Select(v => new JsonObject { ["name"] = v.Name, ["summary"] = v.Summary, ["arrives"] = Contract.M(v.Arrives), ["status"] = v.Status, ["output"] = v.Output })),
        ["exits"] = Array(Contract.Exits.Select(e => new JsonObject { ["code"] = e.Code, ["name"] = e.Name, ["meaning"] = e.Meaning, ["remedy"] = e.Remedy })),
        ["schemas"] = new JsonObject(Schemas().Select(s => KeyValuePair.Create<string, JsonNode?>(s.Id, s.Schema))),
    };

    public static string HelpMarkdown() => string.Join('\n', (string[])
    [
        "# estate " + Contract.Version, "", "Usage: `" + Usage + "`", "", "| Verb | Answers | Status |", "|---|---|---|",
        .. Contract.Verbs.Select(v => "| `" + v.Name + "` | " + v.Summary + " | " + (v.Status == "built" ? "built" : v.Status + ", arrives in " + Contract.Title(v.Arrives)) + " |"),
        "", "| Exit | Name | Meaning | Remedy |", "|---:|---|---|---|",
        .. Contract.Exits.Select(e => "| " + e.Code.ToString(CultureInfo.InvariantCulture) + " | " + e.Name + " | " + e.Meaning + " | " + e.Remedy + " |"),
        "",
    ]);

    /// <summary>An answer as JSON: the envelope, then each property its verb adds, null where this answer carries none.</summary>
    public static JsonObject Json(Envelope answer)
    {
        var json = new JsonObject
        {
            ["schema"] = answer.Schema,
            ["engine"] = Stamped(answer.Stamp?.Engine, answer.Stamp),
            ["receipt"] = answer.Receipt is { } r ? new JsonObject
            {
                ["delta"] = Digest(r.Delta), ["target"] = Digest(r.Target), ["dataFacts"] = r.DataFacts is { } facts ? Digest(facts) : null, ["engine"] = Stamped(r.Engine, answer.Stamp),
                ["profile"] = Digest(r.Profile), ["where"] = r.Where, ["at"] = r.At.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), ["lacking"] = Input(r.Lacking),
            } : null,
            ["verdict"] = new JsonObject { ["outcome"] = answer.Verdict.Outcome, ["message"] = answer.Verdict.Message, ["kind"] = Kind(answer.Verdict.Kind) },
            ["findings"] = Array(answer.Findings.Select(f => new JsonObject { ["code"] = f.Code, ["severity"] = f.Severity, ["subject"] = f.Subject, ["message"] = f.Message, ["remedy"] = f.Remedy })),
            ["exit"] = answer.Exit,
        };
        foreach (var added in Contract.Verbs.FirstOrDefault(v => v.Output == answer.Schema)?.Content ?? new JsonObject())
        {
            json[added.Key] = answer.Content?[added.Key]?.DeepClone();
        }

        return json;
    }

    /// <summary>An answer as Markdown: the verdict's message, then each finding, its message left out where it repeats the verdict's.</summary>
    public static string Markdown(Envelope answer) => string.Concat((string[])
    [
        answer.Verdict.Message, "\n",
        .. answer.Findings.Select(f => "\n- " + f.Severity + " `" + f.Code + "` " + f.Subject + (f.Message == answer.Verdict.Message ? "." : ": " + f.Message) + (f.Remedy is null ? "" : " Remedy: " + f.Remedy) + "\n"),
    ]);

    /// <summary>A fingerprint as the envelope writes it: sha256: and its 64 hex digits.</summary>
    public static string Digest(Fingerprint fingerprint) => "sha256:" + fingerprint;

    /// <summary>The schemas the contract generates, by id: the envelope, the help and each verb's; each is committed under cli/schemas/ as <see cref="SchemaFile"/> names it.</summary>
    public static IReadOnlyList<(string Id, JsonObject Schema)> Schemas() =>
    [
        ("estate.envelope/1", EnvelopeSchema("estate.envelope/1", "What every verb writes with --json: schema, engine, receipt, verdict, findings, exit.", new JsonObject())),
        ("estate.help/1", HelpSchema()),
        .. Contract.Verbs.Where(v => v.Content is not null).Select(v => (v.Output, EnvelopeSchema(v.Output, "What estate " + v.Name + " --json writes: the envelope, and " + string.Join(" and ", v.Content!.Select(p => p.Key)) + ".", v.Content!))),
    ];

    public static string SchemaFile(string id) => id.Replace('/', '.') + ".schema.json";

    private static JsonObject HelpSchema() => Document("estate.help/1", "What estate --help --json writes: the verb table, the exit table and the schemas.", new()
    {
        ["schema"] = new JsonObject { ["const"] = "estate.help/1" },
        ["version"] = Text(),
        ["milestone"] = Pattern("^M[0-9]$"),
        ["usage"] = Text(),
        ["verbs"] = List(Record(new()
        {
            ["name"] = Enum(Contract.Verbs.Select(v => (JsonNode?)v.Name)),
            ["summary"] = Text(),
            ["arrives"] = Pattern("^M[0-9]$"),
            ["status"] = Enum(["built", "stub", "pending"]),
            ["output"] = Pattern(SchemaId),
        })),
        ["exits"] = List(Record(new()
        {
            ["code"] = Enum(Contract.Exits.Select(e => (JsonNode?)e.Code)),
            ["name"] = Enum(Contract.Exits.Select(e => (JsonNode?)e.Name)),
            ["meaning"] = Text(),
            ["remedy"] = Text(),
        })),
        ["schemas"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "object" } },
    });

    /// <summary>The envelope's schema, with the properties a verb adds, each null where an answer carries none (a refusal).</summary>
    private static JsonObject EnvelopeSchema(string id, string description, JsonObject added)
    {
        var envelope = Document(id, description, new()
        {
            ["schema"] = id == "estate.envelope/1" ? Pattern(SchemaId) : new JsonObject { ["const"] = id },
            ["engine"] = Ref("engine"),
            ["receipt"] = Nullable(Ref("receipt")),
            ["verdict"] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("outcome", "message", "kind"), ["properties"] = new JsonObject { ["outcome"] = Text(), ["message"] = Text(), ["kind"] = Nullable(Kinds()) } },
            ["findings"] = List(Ref("finding")),
            ["exit"] = Enum(Contract.Exits.Select(e => (JsonNode?)e.Code)),
        });
        foreach (var property in added)
        {
            ((JsonArray)envelope["required"]!).Add(property.Key);
            envelope["properties"]![property.Key] = Nullable((JsonObject)property.Value!.DeepClone());
        }

        // The envelope alone admits what a verb adds, which the verb's own schema closes.
        envelope["additionalProperties"] = id == "estate.envelope/1";

        // Every refusal carries a remedy: an exit that requires one names at least one finding, each with its remedy.
        // Blocked (§4 row 15) is blocked by the data and names its kind, the row-presence guard or a violation on existing rows; no other exit has a kind.
        var blocked = If(Where("exit", new JsonObject { ["const"] = Contract.Exits.Single(e => e.Name == "blocked").Code }), Where("verdict", Where("kind", Kinds())));
        blocked["else"] = Where("verdict", Where("kind", new JsonObject { ["type"] = "null" }));
        envelope["allOf"] = new JsonArray(
            If(Where("exit", Enum(Contract.Exits.Where(e => e.RemedyRequired).Select(e => (JsonNode?)e.Code))), Where("findings", new JsonObject { ["minItems"] = 1, ["items"] = Where("remedy", Text()) })),
            blocked);
        var finding = Record(new()
        {
            ["code"] = Pattern("^[a-z]+(\\.[a-z0-9-]+)+$"),
            ["severity"] = Enum(["block", "warn", "note"]),
            ["subject"] = Text(),
            ["message"] = Text(),
            ["remedy"] = Nullable(Text()),
        });
        finding["if"] = Where("severity", new JsonObject { ["const"] = "block" });
        finding["then"] = Where("remedy", Text());
        envelope["$defs"] = new JsonObject
        {
            // The kernel's Engine as stamped: the tool, DacFx and the SQL Server image's digest, null when unknown; and the toolchain ledger's pin, UNPINNED while it has none.
            ["engine"] = Record(new() { ["estate"] = Text(), ["dacfx"] = Nullable(Pattern("^[0-9]+(\\.[0-9]+){1,3}$")), ["sqlserver"] = Nullable(Fingerprint()), ["pin"] = Nullable(Text()) }),
            // The kernel's Receipt: §3's five inputs, where and when, and the input it lacks.
            ["receipt"] = Record(new()
            {
                ["delta"] = Fingerprint(), ["target"] = Fingerprint(), ["dataFacts"] = Nullable(Fingerprint()), ["engine"] = Ref("engine"),
                ["profile"] = Fingerprint(), ["where"] = Text(), ["at"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["lacking"] = Nullable(Enum(System.Enum.GetValues<Receipt.Input>().Select(i => (JsonNode?)Input(i)))),
            }),
            ["finding"] = finding,
        };
        return envelope;
    }

    private static JsonObject Stamped(Engine? engine, Stamp? stamp) => new()
    {
        ["estate"] = Contract.Version, ["dacfx"] = engine?.DacFx, ["sqlserver"] = engine?.Image is { } image ? Digest(image) : null, ["pin"] = stamp?.Pin?.ToString(),
    };

    private static JsonObject Document(string id, string description, JsonObject properties) => new(
    [
        new("$schema", "https://json-schema.org/draft/2020-12/schema"), new("title", id), new("description", description),
        new("$comment", "Generated from cli/Contract.cs by Render.Schemas: regenerate with ESTATE_BLESS=1 dotnet test --filter Category=fast; never edit by hand."),
        .. Record(properties).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())),
    ]);

    internal static JsonObject Record(JsonObject properties) =>
        new() { ["type"] = "object", ["additionalProperties"] = false, ["required"] = Array(properties.Select(p => JsonValue.Create(p.Key))), ["properties"] = properties };

    private static JsonObject Where(string property, JsonObject schema) => new() { ["properties"] = new JsonObject { [property] = schema } };
    private static JsonObject If(JsonObject condition, JsonObject then) => new() { ["if"] = condition, ["then"] = then };
    internal static JsonObject List(JsonObject items) => new() { ["type"] = "array", ["items"] = items };
    internal static JsonObject Enum(IEnumerable<JsonNode?> values) => new() { ["enum"] = Array(values) };
    internal static JsonObject Nullable(JsonObject schema) => new() { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "null" }, schema) };
    private static JsonObject Ref(string definition) => new() { ["$ref"] = "#/$defs/" + definition };
    internal static JsonObject Text() => new() { ["type"] = "string", ["minLength"] = 1 };
    internal static JsonObject Pattern(string pattern) => new() { ["type"] = "string", ["pattern"] = pattern };
    internal static JsonObject Fingerprint() => Pattern("^sha256:[0-9a-f]{64}$");
    internal static JsonArray Array(IEnumerable<JsonNode?> items) => new([.. items]);

    /// <summary>A kind as the envelope writes it, null for a verdict the data did not block; the schema's words are these.</summary>
    private static string? Kind(Blocked? kind) => kind switch { null => null, Blocked.Guard => "guard", Blocked.Violation => "violation", _ => throw new System.ArgumentOutOfRangeException(nameof(kind)) };
    private static JsonObject Kinds() => Enum(System.Enum.GetValues<Blocked>().Select(k => (JsonNode?)Kind(k)));

    /// <summary>A receipt's input as the envelope names it, camel-cased: delta, target, dataFacts, engine, profile.</summary>
    private static string? Input(Receipt.Input? input) => input is { } i ? char.ToLowerInvariant(i.ToString()[0]) + i.ToString()[1..] : null;
}
