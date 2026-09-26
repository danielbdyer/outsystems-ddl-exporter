using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DbChange.Kernel;

namespace DbChange.Cli;

/// <summary>The renderers: the help and every answer, as Markdown or as JSON, and the JSON schemas generated from the contract.</summary>
public static class Render
{
    public const string Usage = "dbchange <verb> [arguments] [--json] [--summary] | dbchange --help [--json] | dbchange --version";

    /// <summary>How many entries of each long list, lines and warnings the default answer holds (VALUES.md O11); the whole answer is in the run's answer.json.</summary>
    public const int Shown = 50;

    private const string SchemaId = "^dbchange\\.[a-z]+(-[a-z]+)*/[1-9][0-9]*$";

    /// <summary>The whole answer's file, under the run's folder: what full names when an answer was cut.</summary>
    private static readonly string FullPattern = "^" + Regex.Escape(Io.LocalState.Name) + "/runs/[^/]+/answer\\.json$";

    /// <summary>How a content schema marks a list that can be long, so Cut finds it: the default answer holds its first entries.</summary>
    private const string LongList = "a list that can be long: the default answer holds its first entries, and the run's answer.json the whole list";

    public static JsonObject Help() => new()
    {
        ["schema"] = "dbchange.help/1",
        ["version"] = Contract.Version,
        ["usage"] = Usage,
        ["verbs"] = Array(Contract.Verbs.Select(v => new JsonObject
        {
            ["name"] = v.Name, ["summary"] = v.Summary, ["built"] = v.Built, ["output"] = v.Output,
            ["outcomes"] = Array(v.Answers.Select(o => new JsonObject { ["outcome"] = o.Word, ["exits"] = Array(o.Exits.Select(e => (JsonNode?)e)), ["meaning"] = o.Meaning })),
        })),
        ["exits"] = Array(Contract.Exits.Select(e => new JsonObject { ["code"] = e.Code, ["name"] = e.Name, ["meaning"] = e.Meaning, ["remedy"] = e.Remedy })),
        ["schemas"] = new JsonObject(Schemas().Select(s => KeyValuePair.Create<string, JsonNode?>(s.Id, s.Schema))),
    };

    public static string HelpMarkdown() => string.Join('\n', (string[])
    [
        "# dbchange " + Contract.Version, "", "Usage: `" + Usage + "`", "", "| Verb | Answers | Outcomes | Built |", "|---|---|---|---|",
        .. Contract.Verbs.Select(v => "| `" + v.Name + "` | " + v.Summary + " | " + string.Join(", ", v.Answers.Select(o => o.Word + " (exit " + string.Join(" or ", o.Exits.Select(e => e.ToString(CultureInfo.InvariantCulture))) + ")"))
            + " | " + (v.Built ? "yes" : "no") + " |"),
        "", "| Exit | Name | Meaning | Remedy |", "|---:|---|---|---|",
        .. Contract.Exits.Select(e => "| " + e.Code.ToString(CultureInfo.InvariantCulture) + " | " + e.Name + " | " + e.Meaning + " | " + e.Remedy + " |"),
        "",
    ]);

    /// <summary>
    /// An answer as JSON: the envelope's fields in the order the schema lists them, then each property its verb adds, null where this
    /// answer carries none.
    /// </summary>
    public static JsonObject Json(Envelope answer)
    {
        var json = new JsonObject
        {
            ["schema"] = answer.Schema,
            ["outcome"] = answer.Outcome.Word,
            ["exit"] = answer.Exit,
            ["message"] = answer.Message,
            ["blockedBy"] = Blocked(answer.BlockedBy),
            ["findings"] = Array(answer.Findings.Select(f => new JsonObject { ["code"] = f.Code, ["severity"] = Word(f.Severity), ["subject"] = f.Subject, ["message"] = f.Message, ["remedy"] = f.Remedy })),
            ["version"] = answer.Stamp is null ? null : Contract.Version,
            ["dacfx"] = answer.Stamp?.DacFx.ToString(),
            ["pin"] = answer.Stamp?.Pin?.ToString(),
            ["server"] = Json(answer.Stamp?.Server),
            ["provenance"] = answer.Provenance is { } p ? new JsonObject
            {
                ["change"] = Digest(p.Change), ["schema"] = Digest(p.Schema), ["existingData"] = p.ExistingData is { } data ? Digest(data) : null, ["dacfx"] = p.DacFx.ToString(),
                ["server"] = Json(p.Server), ["publishProfile"] = Digest(p.PublishProfile), ["target"] = p.Target.ToString(),
                ["at"] = p.At.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), ["lacking"] = Array(p.Lacking.Select(i => (JsonNode?)Input(i))),
            } : null,
            ["truncated"] = answer.Truncated,
            ["full"] = answer.Full,
        };
        foreach (var added in Contract.Verbs.FirstOrDefault(v => v.Output == answer.Schema)?.Content ?? new JsonObject())
        {
            json[added.Key] = answer.Content?[added.Key]?.DeepClone();
        }

        return json;
    }

    /// <summary>
    /// An answer as Markdown: the message, or the verb's lines in its place when it has any (diff's change lines, one per line, so M1
    /// exit 2's one line is the whole output); then each finding, its message left out where it repeats the answer's; then, when the
    /// answer was cut, one line naming what was left out and the whole answer's file. Every field passes through
    /// <see cref="Printable"/>; the line feeds and list markers written here are structure and are not escaped.
    /// </summary>
    public static string Markdown(Envelope answer) => string.Concat((string[])
    [
        .. answer.Lines.Count == 0 ? [Printable(answer.Message) + "\n"] : answer.Lines.Select(line => Printable(line) + "\n"),
        .. answer.Findings.Select(f => "\n- " + Word(f.Severity) + " `" + Printable(f.Code) + "` " + Printable(f.Subject) + (f.Message == answer.Message ? "." : ": " + Printable(f.Message))
            + (f.Remedy is null ? "" : " Remedy: " + Printable(f.Remedy)) + "\n"),
        .. answer.Truncated
            ? new[] { "\n… " + answer.LeftOut.ToString("N0", CultureInfo.InvariantCulture) + " more; the whole answer " + (answer.Full is { } full ? "is in " + Printable(full) : "was not written") + ".\n" }
            : [],
    ]);

    /// <summary>A list that can be long, as a content schema marks it: the default answer holds its first <see cref="Shown"/> entries.</summary>
    internal static JsonObject Long(JsonObject items)
    {
        var list = List(items);
        list["$comment"] = LongList;
        return list;
    }

    /// <summary>
    /// The answer cut to what the default form shows: the first <see cref="Shown"/> entries of each list its content schema marks long,
    /// the first Shown lines, every error and note, and the first Shown warnings; with <paramref name="summary"/>, none of the long
    /// lists' entries, no lines and no warnings, so the counts alone stand. The answer's own content is not changed. What was left out
    /// is counted in the cut answer's LeftOut as the entries and warnings left out; the lines mirror the entries (each of diff's lines
    /// is one change of its lists) and are not counted again. The caller writes the whole answer where Full names.
    /// </summary>
    public static Envelope Cut(Envelope answer, bool summary)
    {
        var keep = summary ? 0 : Shown;
        var content = (JsonObject?)answer.Content?.DeepClone();
        var left = 0;
        foreach (var added in content is null ? [] : Contract.Verbs.FirstOrDefault(v => v.Output == answer.Schema)?.Content ?? new JsonObject())
        {
            left += Cut(content![added.Key], (JsonObject)added.Value!, keep);
        }

        var warnings = answer.Findings.Where(f => f.Severity == Severity.Warning).ToList();
        var findings = answer.Findings.Where(f => f.Severity != Severity.Warning || warnings.IndexOf(f) < keep).ToList();
        left += warnings.Count - Math.Min(warnings.Count, keep);
        return answer with { Content = content, Findings = findings, Lines = [.. answer.Lines.Take(keep)], LeftOut = left };
    }

    /// <summary>The entries left out of each long list under <paramref name="schema"/> in <paramref name="json"/>, cut in place to its first <paramref name="keep"/>.</summary>
    private static int Cut(JsonNode? json, JsonObject schema, int keep)
    {
        if (schema["anyOf"] is JsonArray branches)
        {
            return branches.OfType<JsonObject>().Where(b => (string?)b["type"] != "null").Sum(b => Cut(json, b, keep));
        }

        if ((string?)schema["$comment"] == LongList && json is JsonArray list)
        {
            var left = Math.Max(0, list.Count - keep);
            for (var i = list.Count - 1; i >= keep; i--)
            {
                list.RemoveAt(i);
            }

            return left;
        }

        return (string?)schema["type"] == "object" && schema["properties"] is JsonObject properties && json is JsonObject record
            ? properties.Sum(p => Cut(record[p.Key], (JsonObject)p.Value!, keep))
            : 0;
    }

    /// <summary>
    /// A JSON answer as text: io/Json's canonical form, with each bidirectional control character written as \uXXXX as well.
    /// System.Text.Json escapes category Cc itself and writes U+200E, U+202E and the rest of Unicode's Bidi_Control raw; JSON's
    /// structure is ASCII, so those twelve code points occur only inside strings, where the escape is valid and a parser restores
    /// the character. A lone surrogate is written as � by System.Text.Json; the kernel's comparison and fingerprint keep the exact name.
    /// </summary>
    public static string JsonText(JsonObject json)
    {
        var text = Io.Json.Text(json);
        var escaped = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (BidiControl(c))
            {
                escaped.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }

    /// <summary>
    /// Text as Markdown and a terminal may show it (decision 2.25): each character of category Cc (U+0000 to U+001F, U+007F to
    /// U+009F), each with the Bidi_Control property (U+061C, U+200E, U+200F, U+202A to U+202E, U+2066 to U+2069, which reorder what
    /// a terminal or a pull-request page shows) and each lone surrogate written as \u and four upper-case hex digits, and every other
    /// character as it is, so a name holding ESC, a tab or a line break reads on one line and a surrogate pair prints whole.
    /// </summary>
    public static string Printable(string text)
    {
        var printable = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                printable.Append(c).Append(text[++i]);
            }
            else if (char.IsControl(c) || char.IsSurrogate(c) || BidiControl(c))
            {
                printable.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                printable.Append(c);
            }
        }

        return printable.ToString();
    }

    /// <summary>Unicode's Bidi_Control property: the twelve characters that change the direction text is shown in.</summary>
    private static bool BidiControl(char c) => c is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    /// <summary>A fingerprint as the envelope writes it: sha256: and its 64 hex digits.</summary>
    public static string Digest(Fingerprint fingerprint) => "sha256:" + fingerprint;

    /// <summary>The schemas the contract generates, by id: the envelope, the help and each verb's; each is committed under cli/schemas/ as <see cref="SchemaFile"/> names it.</summary>
    public static IReadOnlyList<(string Id, JsonObject Schema)> Schemas() =>
    [
        ("dbchange.envelope/1", EnvelopeSchema("dbchange.envelope/1", "What every verb writes with --json: schema, outcome, exit, message, blockedBy, findings, version, dacfx, pin, server, provenance, truncated, full.", null)),
        ("dbchange.help/1", HelpSchema()),
        .. Contract.Verbs.Where(v => v.Content is not null).Select(v => (v.Output, EnvelopeSchema(v.Output, "What dbchange " + v.Name + " --json writes: the envelope, and " + string.Join(" and ", v.Content!.Select(p => p.Key)) + ".", v))),
    ];

    public static string SchemaFile(string id) => id.Replace('/', '.') + ".schema.json";

    private static JsonObject HelpSchema() => Document("dbchange.help/1", "What dbchange --help --json writes: the verb table, the exit table and the schemas.", new()
    {
        ["schema"] = new JsonObject { ["const"] = "dbchange.help/1" },
        ["version"] = Text(),
        ["usage"] = Text(),
        ["verbs"] = List(Record(new()
        {
            ["name"] = Enum(Contract.Verbs.Select(v => (JsonNode?)v.Name)),
            ["summary"] = Text(),
            ["built"] = new JsonObject { ["type"] = "boolean" },
            ["output"] = Pattern(SchemaId),
            ["outcomes"] = List(Record(new() { ["outcome"] = Text(), ["exits"] = List(Enum(Contract.Exits.Select(e => (JsonNode?)e.Code))), ["meaning"] = Text() })),
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

    /// <summary>
    /// The envelope's schema: the generic one, which admits every verb's outcome word and any verb's property, or a verb's own, which
    /// admits its words and closes its properties, each null where an answer carries none (an error). Each rule is a minimal pair in
    /// ContractTests.EnvelopePairs.
    /// </summary>
    private static JsonObject EnvelopeSchema(string id, string description, Verb? verb)
    {
        // An outcome word and the exits it may take: each verb row's words with their exits, and each failure exit's name at that exit, where 0 is no failure.
        var outcomes = (verb is null ? Contract.Verbs : [verb]).SelectMany(v => v.Answers.Select(o => (Word: o.Word, Exits: o.Exits.AsEnumerable())))
            .Concat(Contract.Exits.Where(e => e.Code != 0).Select(e => (Word: e.Name, Exits: (IEnumerable<int>)[e.Code])))
            .GroupBy(o => o.Word, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Word: g.Key, Exits: g.SelectMany(o => o.Exits).Distinct().Order().ToList())).ToList();
        var envelope = Document(id, description, new()
        {
            ["schema"] = verb is null ? Pattern(SchemaId) : new JsonObject { ["const"] = id },
            ["outcome"] = Enum(outcomes.Select(o => (JsonNode?)o.Word)),
            ["exit"] = Enum(Contract.Exits.Select(e => (JsonNode?)e.Code)),
            ["message"] = Text(),
            ["blockedBy"] = Nullable(BlockedBys()),
            ["findings"] = List(Ref("finding")),
            ["version"] = Nullable(Text()),
            ["dacfx"] = Nullable(Pattern(DacFxVersion)),
            ["pin"] = Nullable(Pattern("^UNPINNED$|" + DacFxVersion)),
            ["server"] = Nullable(Ref("server")),
            ["provenance"] = Nullable(Ref("provenance")),
            ["truncated"] = new JsonObject { ["type"] = "boolean" },
            ["full"] = Nullable(Pattern(FullPattern)),
        });
        foreach (var property in verb?.Content ?? new JsonObject())
        {
            ((JsonArray)envelope["required"]!).Add(property.Key);
            envelope["properties"]![property.Key] = Nullable((JsonObject)property.Value!.DeepClone());
        }

        // The envelope alone admits what a verb adds, which the verb's own schema closes.
        envelope["additionalProperties"] = verb is null;

        // Blocked (§4 row 15) names what blocked it, BlockOnPossibleDataLoss or a constraint violation, at exit 3 and at no other.
        var blocked = If(Where("exit", new JsonObject { ["const"] = Contract.Exits.Single(e => e.Name == "blocked").Code }), Where("blockedBy", BlockedBys()));
        blocked["else"] = Where("blockedBy", new JsonObject { ["type"] = "null" });
        envelope["allOf"] = new JsonArray(
        [
            // Every error carries a remedy: an exit that requires one names at least one finding, each with its remedy.
            If(Where("exit", Enum(Contract.Exits.Where(e => e.RemedyRequired).Select(e => (JsonNode?)e.Code))), Where("findings", new JsonObject { ["minItems"] = 1, ["items"] = Where("remedy", Text()) })),
            blocked,
            // Each outcome word ties to the exits it may take.
            .. outcomes.Select(o => If(Where("outcome", new JsonObject { ["const"] = o.Word }), Where("exit", Enum(o.Exits.Select(e => (JsonNode?)e))))),
            // An answer that names its whole file was cut; an answer that was not cut names none.
            If(Where("full", new JsonObject { ["type"] = "string" }), Where("truncated", new JsonObject { ["const"] = true })),
            If(Where("truncated", new JsonObject { ["const"] = false }), Where("full", new JsonObject { ["type"] = "null" })),
        ]);

        // The kernel's Finding: its code in the one code pattern, its severity one of the three words, and, on every error, a remedy, which the kernel requires by construction and the schema states.
        var finding = Record(new()
        {
            ["code"] = Pattern(ErrorCode.Pattern),
            ["severity"] = Enum(System.Enum.GetValues<Severity>().Select(s => (JsonNode?)Word(s))),
            ["subject"] = Text(),
            ["message"] = Text(),
            ["remedy"] = Nullable(Text()),
        });
        finding["if"] = Where("severity", new JsonObject { ["const"] = Word(Severity.Error) });
        finding["then"] = Where("remedy", Text());
        // The kernel's Provenance: the inputs a claim stands on, the target and when, and the inputs it lacks; an input is null exactly when lacking names it.
        var provenance = Record(new()
        {
            ["change"] = Fingerprint(), ["schema"] = Fingerprint(), ["existingData"] = Nullable(Fingerprint()), ["dacfx"] = Pattern(DacFxVersion), ["server"] = Nullable(Ref("server")),
            ["publishProfile"] = Fingerprint(), ["target"] = Text(), ["at"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
            ["lacking"] = new JsonObject { ["type"] = "array", ["uniqueItems"] = true, ["items"] = Enum(System.Enum.GetValues<Provenance.Input>().Select(i => (JsonNode?)Input(i))) },
        });
        provenance["allOf"] = new JsonArray([.. ((Provenance.Input[])[Provenance.Input.ExistingData, Provenance.Input.Server]).Select(Lacks)]);
        envelope["$defs"] = new JsonObject
        {
            // The kernel's Server: the product version SQL Server reports, the database's compatibility level, and the image's digest for a copy on the dbchange-sql container.
            ["server"] = Record(new()
            {
                ["version"] = Pattern("^[0-9]{1,5}(\\.[0-9]{1,5}){3}$"), ["compatibilityLevel"] = new JsonObject { ["type"] = "integer" }, ["image"] = Nullable(Fingerprint()),
            }),
            ["provenance"] = provenance,
            ["finding"] = finding,
        };
        return envelope;
    }

    /// <summary>A DacFx release version as the envelope writes one: two to four dot-separated groups of digits, 170.5.96.</summary>
    private const string DacFxVersion = "^[0-9]+(\\.[0-9]+){1,3}$";

    /// <summary>The rule that a provenance's input is null exactly when its lacking names it.</summary>
    private static JsonObject Lacks(Provenance.Input input)
    {
        var rule = If(Where("lacking", new JsonObject { ["contains"] = new JsonObject { ["const"] = Input(input) } }), Where(Input(input), new JsonObject { ["type"] = "null" }));
        rule["else"] = Where(Input(input), new JsonObject { ["not"] = new JsonObject { ["type"] = "null" } });
        return rule;
    }

    /// <summary>A server as the envelope writes it, or null.</summary>
    private static JsonObject? Json(Server? server) => server is null ? null : new JsonObject
    {
        ["version"] = server.Version, ["compatibilityLevel"] = server.CompatibilityLevel, ["image"] = server.Image is { } image ? Digest(image) : null,
    };

    private static JsonObject Document(string id, string description, JsonObject properties) => new(
    [
        new("$schema", "https://json-schema.org/draft/2020-12/schema"), new("title", id), new("description", description),
        new("$comment", "Generated from cli/Contract.cs by Render.Schemas: regenerate with DBCHANGE_BLESS=1 dotnet test --filter Category=fast; never edit by hand."),
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

    /// <summary>A severity as the envelope writes it: error, warning, note.</summary>
#pragma warning disable CS8524
    private static string Word(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        Severity.Note => "note",
    };

    /// <summary>What blocked an answer, as the envelope writes it: block-on-possible-data-loss or constraint-violation.</summary>
    private static string Word(BlockedBy blockedBy) => blockedBy switch
    {
        BlockedBy.BlockOnPossibleDataLoss => "block-on-possible-data-loss",
        BlockedBy.ConstraintViolation => "constraint-violation",
    };
#pragma warning restore CS8524

    /// <summary>What blocked an answer, or null off exit 3.</summary>
    private static string? Blocked(BlockedBy? blockedBy) => blockedBy is { } b ? Word(b) : null;

    private static JsonObject BlockedBys() => Enum(System.Enum.GetValues<BlockedBy>().Select(b => (JsonNode?)Word(b)));

    /// <summary>A provenance's input as the envelope names it, camel-cased: change, schema, existingData, dacFx, server, publishProfile.</summary>
    private static string Input(Provenance.Input input) => input switch
    {
        Provenance.Input.DacFx => "dacfx",
        _ => char.ToLowerInvariant(input.ToString()[0]) + input.ToString()[1..],
    };
}
