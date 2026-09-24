using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

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

    public static JsonObject Json(Envelope answer) => new()
    {
        ["schema"] = answer.Schema,
        ["engine"] = answer.Engine.DeepClone(),
        ["receipt"] = answer.Receipt?.DeepClone(),
        ["verdict"] = new JsonObject { ["outcome"] = answer.Verdict.Outcome, ["message"] = answer.Verdict.Message, ["kind"] = Kind(answer.Verdict.Kind) },
        ["findings"] = Array(answer.Findings.Select(f => new JsonObject { ["code"] = f.Code, ["severity"] = f.Severity, ["subject"] = f.Subject, ["message"] = f.Message, ["remedy"] = f.Remedy })),
        ["exit"] = answer.Exit,
    };

    public static string Markdown(Envelope answer) => string.Concat((string[])
    [
        answer.Verdict.Message, "\n",
        .. answer.Findings.Select(f => "\n- " + f.Severity + " `" + f.Code + "` " + f.Subject + ": " + f.Message + (f.Remedy is null ? "" : " Remedy: " + f.Remedy) + "\n"),
    ]);

    /// <summary>The schemas the contract generates, by id; each is committed under cli/schemas/ as <see cref="SchemaFile"/> names it.</summary>
    public static IReadOnlyList<(string Id, JsonObject Schema)> Schemas() => [("estate.envelope/1", EnvelopeSchema()), ("estate.help/1", HelpSchema())];

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

    private static JsonObject EnvelopeSchema()
    {
        var envelope = Document("estate.envelope/1", "What every verb writes with --json: schema, engine, receipt, verdict, findings, exit.", new()
        {
            ["schema"] = Pattern(SchemaId),
            ["engine"] = Ref("engine"),
            ["receipt"] = Nullable(Ref("receipt")),
            ["verdict"] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("outcome", "message", "kind"), ["properties"] = new JsonObject { ["outcome"] = Text(), ["message"] = Text(), ["kind"] = Nullable(Kinds()) } },
            ["findings"] = List(Ref("finding")),
            ["exit"] = Enum(Contract.Exits.Select(e => (JsonNode?)e.Code)),
        });

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
            // Pinned for the kernel's Engine and Receipt: the tool, DacFx and SQL Server; §3's five inputs, where and when.
            ["engine"] = Record(new() { ["estate"] = Text(), ["dacfx"] = Nullable(Text()), ["sqlserver"] = Nullable(Text()) }),
            ["receipt"] = Record(new()
            {
                ["delta"] = Fingerprint(), ["target"] = Fingerprint(), ["dataFacts"] = Nullable(Fingerprint()), ["engine"] = Ref("engine"),
                ["profile"] = Fingerprint(), ["where"] = Text(), ["at"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
            }),
            ["finding"] = finding,
        };
        return envelope;
    }

    private static JsonObject Document(string id, string description, JsonObject properties) => new(
    [
        new("$schema", "https://json-schema.org/draft/2020-12/schema"), new("title", id), new("description", description),
        new("$comment", "Generated from cli/Contract.cs by Render.Schemas: regenerate with ESTATE_BLESS=1 dotnet test --filter Category=fast; never edit by hand."),
        .. Record(properties).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())),
    ]);

    private static JsonObject Record(JsonObject properties) =>
        new() { ["type"] = "object", ["additionalProperties"] = false, ["required"] = Array(properties.Select(p => JsonValue.Create(p.Key))), ["properties"] = properties };

    private static JsonObject Where(string property, JsonObject schema) => new() { ["properties"] = new JsonObject { [property] = schema } };
    private static JsonObject If(JsonObject condition, JsonObject then) => new() { ["if"] = condition, ["then"] = then };
    private static JsonObject List(JsonObject items) => new() { ["type"] = "array", ["items"] = items };
    private static JsonObject Enum(IEnumerable<JsonNode?> values) => new() { ["enum"] = Array(values) };
    private static JsonObject Nullable(JsonObject schema) => new() { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "null" }, schema) };
    private static JsonObject Ref(string definition) => new() { ["$ref"] = "#/$defs/" + definition };
    private static JsonObject Text() => new() { ["type"] = "string", ["minLength"] = 1 };
    private static JsonObject Pattern(string pattern) => new() { ["type"] = "string", ["pattern"] = pattern };
    private static JsonObject Fingerprint() => Pattern("^sha256:[0-9a-f]{64}$");
    private static JsonArray Array(IEnumerable<JsonNode?> items) => new([.. items]);

    /// <summary>A kind as the envelope writes it, null for a verdict the data did not block; the schema's words are these.</summary>
    private static string? Kind(Blocked? kind) => kind switch { null => null, Blocked.Guard => "guard", Blocked.Violation => "violation", _ => throw new System.ArgumentOutOfRangeException(nameof(kind)) };
    private static JsonObject Kinds() => Enum(System.Enum.GetValues<Blocked>().Select(k => (JsonNode?)Kind(k)));
}
