using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Estate.Kernel;

namespace Estate.Cli;

/// <summary>A row of the verb table: the verb, the question it answers, the milestone it arrives in, and its body once built.</summary>
public sealed record Verb(string Name, string Summary, int Arrives, Func<IReadOnlyList<string>, Envelope>? Body = null)
{
    /// <summary>The schema its --json answer names, such as estate.doctor/1.</summary>
    public string Output => "estate." + Name.TrimStart('-') + "/1";

    /// <summary>built; stub, a body ahead of its milestone; pending, no body yet.</summary>
    public string Status => Body is null ? "pending" : Arrives > Contract.Milestone ? "stub" : "built";
}

/// <summary>A row of the frozen exit table: codes are added, never removed or renumbered (cli/exits.frozen).</summary>
public sealed record ExitCode(int Code, string Name, string Meaning, string Remedy, bool RemedyRequired);

/// <summary>
/// What every verb writes with --json. Engine and Receipt are JSON nodes until WP 1.7 binds the kernel's
/// Engine and Receipt (WP 0.3); cli/schemas/estate.envelope.1.schema.json pins their shape.
/// </summary>
public sealed record Envelope(string Schema, JsonNode Engine, JsonNode? Receipt, Verdict Verdict, IReadOnlyList<Finding> Findings, int Exit);

/// <summary>The answer in a line; at exit 3 its kind says how the data blocked, and no other verdict has one.</summary>
public sealed record Verdict(string Outcome, string Message, Blocked? Kind = null);

/// <summary>How the data blocked (§4 row 15): the publish guard refused because the table has rows, or the engine refused the change on existing rows (Msg 547, Msg 2628).</summary>
public enum Blocked { Guard, Violation }

/// <summary>A finding; its remedy is a verb or a file path, and one that blocks carries one.</summary>
public sealed record Finding(string Code, string Severity, string Subject, string Message, string? Remedy);

/// <summary>The contract as data: the verb table and the exit table. --help --json and cli/schemas/ are generated from them.</summary>
public static class Contract
{
    /// <summary>The milestone this build completes.</summary>
    public const int Milestone = 0;

    private static readonly string[] Milestones = ["Ground", "Read", "Predict", "Twin", "Prove", "Record and gate", "After deploy", "Front door"];

    public static string Version { get; } = typeof(Contract).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    public static readonly IReadOnlyList<Verb> Verbs =
    [
        new("doctor", "Can this machine do the work: the SDK, the engine, the substrate.", 1, _ => Doctor(Io.Doctor.Examine())),
        new("read", "What a schema is, from a ref, a package or a database, read whole, with its fingerprint.", 1),
        new("diff", "What changes between two schemas, property by property, deploy scripts and refactorlog included.", 1),
        new("classify", "Which operation a change is, provisionally, from the committed evidence.", 2),
        new("predict", "Whether a change blocks or applies on each environment the caller can read, and why.", 2),
        new("profile", "What an environment's data looks like, as the evidence the Twin is minted from.", 3),
        new("twin", "A current substrate, stood up from the repository at a ref.", 3),
        new("prove", "What the engine does with a change on a fresh copy, with a receipt.", 4),
        new("record", "The pull request body, rendered from the receipts.", 5),
        new("gate", "The pull request's proof, reproduced from the clone.", 5),
        new("check", "Whether an environment has drifted, and whether the platform, the evidence and the locks agree.", 1),
        new("knowledge", "The knowledge tree, packaged for each agent and vendored to the estate.", 5),
        new("--version", "The tool's version.", 0, _ => Answer("estate.version/1", "done", "estate " + Version, [], 0)),
    ];

    public static readonly IReadOnlyList<ExitCode> Exits =
    [
        new(0, "done", "Done: the verb did its work.", "nothing to do", false),
        new(1, "bad-arguments", "Bad arguments: an unknown verb, flag or value.", "estate --help", false),
        new(2, "unparsed-input", "An input could not be parsed: a schema, a configuration file or a project.", "the file and line the finding names", true),
        new(3, "blocked", "Blocked by the data, a finding and not a failure: kind guard, the publish guard refused because the table has rows; or kind violation, the engine refused the change on existing rows (Msg 547, Msg 2628).", "the site the finding names: the operation's two-release shape, or the rows it counts", false),
        new(4, "unreachable", "The target is unreachable: no substrate, or SQL Server, Docker or LocalDB not answering.", "estate doctor; estate twin up", true),
        new(5, "differs", "Divergence found: the target differs from the repository; the findings name each differing object.", "the objects the findings name", false),
        new(6, "configuration-refused", "The environment or configuration is refused: the .NET SDK missing, an unknown key, a literal credential, an engine outside the pinned window, or a verb this build does not have yet.", "estate doctor, or the file or milestone the finding names", true),
        new(7, "build-failed", "The build failed; the findings carry the build's errors.", "the file and error the finding names", false),
        new(9, "refused-by-name", "Refused by name: a named environment, an unregistered copy or a lock; the refusal's code is printed.", "the refusal's own remedy", true),
        new(130, "interrupted", "Interrupted (Ctrl-C or --timeout); the cleanup ran and the state is as before.", "run the verb again", false),
    ];

    /// <summary>
    /// The refusal table: the exit a refusal takes, by its code's area, the word before the first dot. io names what it refused
    /// (sdk.missing, build.failed), and this table alone says how estate exits for it; ContractTests finds each code io writes.
    /// The posture, a profile, a reference, a connection and a SQLCMD value are configuration (exit 6), whether io or the kernel
    /// refuses them. A target of no known form is a bad argument; a server that does not answer or refuses the identity is exit 4;
    /// a copy the registry does not hold, a substrate on a named host, and a probe the allowlist refuses are refused by name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> RefusalExits = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["ref"] = 1,
        ["target"] = 1,
        ["package"] = 2,
        ["refactorlog"] = 2,
        ["walk"] = 2,
        ["registry"] = 2,
        ["origin"] = 4,
        ["server"] = 4,
        ["substrate"] = 4,
        ["git"] = 6,
        ["sdk"] = 6,
        ["tool"] = 6,
        ["posture"] = 6,
        ["profile"] = 6,
        ["reference"] = 6,
        ["connection"] = 6,
        ["sqlcmd"] = 6,
        ["twin"] = 6,
        ["build"] = 7,
        ["branch"] = 9,
        ["copy"] = 9,
        ["probe"] = 9,
    };

    public static int Exit(Refusal refusal) => RefusalExits[refusal.Code.Split('.')[0]];

    /// <summary>A milestone as the plan writes it, M2; and as a person reads it, M2 (Predict).</summary>
    public static string M(int milestone) => "M" + milestone.ToString(CultureInfo.InvariantCulture);

    public static string Title(int milestone) => M(milestone) + " (" + Milestones[milestone] + ")";

    /// <summary>An answer with no receipt, from an engine that is only the tool: nothing here loads DacFx or reaches SQL Server.</summary>
    public static Envelope Answer(string schema, string outcome, string message, IReadOnlyList<Finding> findings, int exit) =>
        new(schema, new JsonObject { ["estate"] = Version, ["dacfx"] = null, ["sqlserver"] = null }, null, new Verdict(outcome, message), findings, exit);

    public static Envelope NotBuilt(Verb verb) => Answer(verb.Output, "not-built", "estate " + verb.Name + " arrives in " + Title(verb.Arrives) + ".",
        [new("verb.not-built", "block", "estate " + verb.Name, "The verb is in the contract; its body arrives in " + Title(verb.Arrives) + ".", "estate --help lists what this build runs")], 6);

    public static Envelope UnknownVerb(string word) => Answer("estate.envelope/1", "bad-arguments", "'" + word + "' is not a verb of estate.",
        [new("arguments.unknown-verb", "block", "estate " + word, "The verb table has no row named '" + word + "'.", "estate --help")], 1);

    /// <summary>M0's doctor (M0 exit 3): DEGRADED, since the checks beyond io/Doctor's are M1's; a blocking finding with its remedy per item missing.</summary>
    public static Envelope Doctor(IReadOnlyList<Io.Doctor.Check> checks) => Answer("estate.doctor/1", "degraded",
        string.Join(" | ", (string[])["estate doctor DEGRADED", .. checks.Select(c => c.Item + "=" + c.Found), "checks beyond these arrive in " + Title(1)]),
        [
            .. checks.Where(c => c.Remedy is not null).Select(c => new Finding("doctor." + c.Item, "block", "estate doctor", c.Item + ": " + c.Found + ".", c.Remedy)),
            new("doctor.not-built", "warn", "estate doctor", "The engine against estate/ledgers/toolchain.md, the Twin and the estate checkout are not built until " + Title(1) + ".", "estate --help lists what this build runs"),
        ], 6);
}
