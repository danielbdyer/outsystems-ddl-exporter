using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Estate.Kernel;

namespace Estate.Cli;

/// <summary>
/// A row of the verb table: the verb, the question it answers, its body once built, the schema of what it adds to the envelope, and the
/// outcomes its answers take. A verb is built when it has a body, and the help says so and nothing of when the rest arrive (DECISIONS.md,
/// 2026-09-25). A failure's outcome is its exit's name and is not listed here.
/// </summary>
public sealed record Verb(string Name, string Summary, Func<Checkout, IReadOnlyList<string>, Envelope>? Body = null, JsonObject? Content = null, IReadOnlyList<Outcome>? Outcomes = null)
{
    /// <summary>The schema its --json answer names, such as estate.doctor/1.</summary>
    public string Output => "estate." + Name.TrimStart('-') + "/1";

    /// <summary>Whether this build has the verb: it has a body.</summary>
    public bool Built => Body is not null;

    /// <summary>The outcomes this verb's answers take, a closed set its schema enumerates and ties to its exits; none for a verb with no body.</summary>
    public IReadOnlyList<Outcome> Answers => Outcomes ?? [];

    /// <summary>The outcome of this verb's answer that reads as <paramref name="word"/>; a word the row does not list is a defect in estate, found by the first test that renders it.</summary>
    public Outcome Outcome(string word) => Answers.FirstOrDefault(o => o.Word == word)
        ?? throw new InvalidOperationException("estate " + Name + " answers " + string.Join(", ", Answers.Select(o => o.Word)) + ", and '" + word + "' is none of them.");
}

/// <summary>An outcome an answer takes: the word the envelope writes, the exits it may take, and what it means to the reader.</summary>
public sealed record Outcome(string Word, IReadOnlyList<int> Exits, string Meaning)
{
    /// <summary>A failure's outcome: its exit's name, at that exit alone.</summary>
    public static Outcome Of(ExitCode exit) => new(exit.Name, [exit.Code], exit.Meaning);
}

/// <summary>
/// Where estate runs: the estate's root (Posture.Root of the working directory), the working directory, the tool folder ESTATE_TOOL
/// names, if any, and the run's query log, which names the run's folder under .estate/runs/ and which Program.Run begins once per
/// command, so a verb's statements and the whole answer it was cut from sit in one folder.
/// </summary>
public sealed record Checkout(string Root, string WorkingDirectory, string? Tool, Io.SqlServer.QueryLog? Log = null)
{
    public static Checkout Here() => new(Io.Posture.Root(Directory.GetCurrentDirectory()), Directory.GetCurrentDirectory(), Environment.GetEnvironmentVariable("ESTATE_TOOL"));

    /// <summary>The run's query log: the one begun for the command, or a new one when this checkout was made outside Program.Run.</summary>
    public Io.SqlServer.QueryLog Run => Log ?? Io.SqlServer.QueryLog.Start(Root);
}

/// <summary>A row of the frozen exit table: codes are added, never removed or renumbered (cli/exits.frozen).</summary>
public sealed record ExitCode(int Code, string Name, string Meaning, string Remedy, bool RemedyRequired);

/// <summary>
/// How the data blocked (§4 row 15), at exit 3 alone: the data-loss check (BlockOnPossibleDataLoss) stopped the publish because the
/// table has rows, or SQL Server refused the change on existing rows (Msg 547, Msg 2628), a constraint violation. Written as
/// data-loss-check and constraint-violation; no built verb exits 3, and prove (M4) is its first user.
/// </summary>
public enum BlockedBy
{
    DataLossCheck,
    ConstraintViolation,
}

/// <summary>
/// What every verb writes with --json: its schema; the outcome, a word of the verb's closed set or a failure's exit name, with the
/// exit the outcome admits; the answer in one line; what blocked it, at exit 3 alone; the findings; the stamp (DacFx, the pin and the
/// SQL Server, as far as the verb's work got) and the provenance of the claim, when the answer makes one; whether the answer was cut
/// to its first entries and where the whole one is; and what the verb adds (its Content), with the lines a verb prints after its
/// message (diff's change lines). The constructor refuses an exit its outcome does not list and a blockedBy off exit 3, so an answer
/// that says matches at exit 5 is a defect found by the first test that renders it.
/// </summary>
public sealed record Envelope
{
    public Envelope(string schema, Outcome outcome, int exit, string message, IReadOnlyList<Finding> findings, BlockedBy? blockedBy = null, Stamp? stamp = null,
        Provenance? provenance = null, JsonObject? content = null, IReadOnlyList<string>? lines = null)
    {
        if (!outcome.Exits.Contains(exit))
        {
            throw new ArgumentException("An answer whose outcome is '" + outcome.Word + "' exits " + string.Join(" or ", outcome.Exits.Select(e => e.ToString(CultureInfo.InvariantCulture)))
                + ", and this one exits " + exit.ToString(CultureInfo.InvariantCulture) + ".", nameof(exit));
        }

        if ((exit == 3) != blockedBy.HasValue)
        {
            throw new ArgumentException("An answer names what blocked it exactly at exit 3, and this one exits " + exit.ToString(CultureInfo.InvariantCulture) + ".", nameof(blockedBy));
        }

        (Schema, Outcome, Exit, Message, Findings, BlockedBy, Stamp, Provenance, Content, Lines) = (schema, outcome, exit, message, findings, blockedBy, stamp, provenance, content, lines ?? []);
    }

    public string Schema { get; init; }

    public Outcome Outcome { get; init; }

    public int Exit { get; init; }

    /// <summary>The answer in one line.</summary>
    public string Message { get; init; }

    public IReadOnlyList<Finding> Findings { get; init; }

    public BlockedBy? BlockedBy { get; init; }

    /// <summary>What the answer stands on beside the tool's version; null for an answer that stands on the tool alone, which loaded no DacFx.</summary>
    public Stamp? Stamp { get; init; }

    /// <summary>What the claim stands on, when the answer makes one.</summary>
    public Provenance? Provenance { get; init; }

    public JsonObject? Content { get; init; }

    /// <summary>The Markdown body a verb prints in its message's place: diff's change lines; empty for the others.</summary>
    public IReadOnlyList<string> Lines { get; init; }

    /// <summary>How many entries of a long list, lines and warnings the answer was cut by (Render.Cut); 0 for an answer shown whole.</summary>
    public int LeftOut { get; init; }

    /// <summary>Whether the answer was cut to its first entries, lines and warnings (Render.Shown); the whole answer is then in <see cref="Full"/>.</summary>
    public bool Truncated => LeftOut > 0;

    /// <summary>The run's answer.json holding the whole answer, from the estate's root with '/', when the answer was cut and the file could be written.</summary>
    public string? Full { get; init; }
}

/// <summary>The contract as data: the verb table and the exit table. --help --json and cli/schemas/ are generated from them.</summary>
public static class Contract
{
    public static string Version { get; } = typeof(Contract).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    public static readonly IReadOnlyList<Verb> Verbs =
    [
        new("doctor", "Can this machine do the work: the SDK and runtime, the tool and its DacFx against the toolchain ledger, the build route, the scratch server, Git LFS. estate doctor",
            Cli.Verbs.Doctor, Cli.Verbs.DoctorContent,
            [new("ready", [0], "every prerequisite is present"), new("degraded", [6], "a prerequisite is missing; a finding names each, with its remedy")]),
        new("read", "What a schema is, from a ref, a package or a database, read whole, with its fingerprint. estate read --from <target> [--project <path>]",
            Cli.Verbs.Read, Cli.Verbs.ReadContent, [new("done", [0], "the target was read whole; read holds its elements and fingerprint")]),
        new("diff", "What changes between two schemas, property by property, deploy scripts and refactorlog included. estate diff --from <target> --to <target> [--project <path>] [--fail-on-change]",
            Cli.Verbs.Diff, Cli.Verbs.DiffContent,
            [new("in-sync", [0], "the two schemas hold the same elements"), new("differs", [0, 5], "the two schemas differ; exit 5 only with --fail-on-change")]),
        new("classify", "Which operation a change is, provisionally, from the committed evidence."),
        new("predict", "Whether a change blocks or applies on each environment the caller can read, and why."),
        new("measure", "What an environment's data looks like, as the evidence the synthetic copy is generated from."),
        new("synthetic-copy", "A copy on the scratch server, built from the repository at a ref and filled with rows generated from the measured data."),
        new("prove", "What the engine does with a change on a fresh copy, with a receipt."),
        new("describe", "The pull request description, rendered from the receipts."),
        new("gate", "The pull request's proof, reproduced from the clone."),
        new("check", "Whether a database has drifted from the repository at a ref; the platform, the evidence and the locks arrive later. estate check drift --target <target> --at <ref> "
            + "[--profile <path>] [--project <path>]", Cli.Verbs.Check, Cli.Verbs.CheckContent,
            [new("in-sync", [0], "the deploy plan against the target is empty"), new("differs", [5], "the deploy plan holds operations; a finding names each object")]),
        new("knowledge", "The knowledge tree, packaged for each agent and vendored to the estate."),
        new("--version", "The tool's version.", (_, _) => Answer("estate.version/1", new Outcome("done", [0], "the message is the tool's version"), 0, "estate " + Version, []),
            Outcomes: [new("done", [0], "the message is the tool's version")]),
    ];

    public static readonly IReadOnlyList<ExitCode> Exits =
    [
        new(0, "done", "Done: the verb did its work.", "Take no action.", false),
        new(1, "bad-arguments", "Bad arguments: an unknown verb, flag or value.", "Run estate --help to see the verbs and each one's flags.", false),
        new(2, "unparsed-input", "An input could not be parsed: a schema, a configuration file or a project.", "Correct the file at the line the finding names.", true),
        new(3, "blocked", "Blocked by the data, a finding and not a failure: blocked by the data-loss check, which stopped the publish because the table has rows; or by a constraint violation, SQL Server refusing the change on existing rows (Msg 547, Msg 2628).", "Change the operation at the site the finding names: its two-release shape, or the rows it counts.", false),
        new(4, "unreachable", "The target is unreachable: no scratch server, or SQL Server, Docker or LocalDB not answering.", "Run estate doctor, then start the scratch server with ci/sql.sh up (ci/sql.ps1 up on Windows).", true),
        new(5, "differs", "Divergence found: the target differs from the repository; the findings name each differing object.", "Correct the target or the repository at the objects the findings name.", false),
        new(6, "configuration-refused", "The environment or configuration is refused: the .NET SDK missing, an unknown key, a literal credential, an engine outside the pinned window, a verb this build does not have yet, a failure DacFx reports with no SQL Server error inside (dacfx.failed), or a defect in estate itself (internal.unexpected).", "Run estate doctor, or correct the file the finding names.", true),
        new(7, "build-failed", "The build failed; the findings carry the build's errors.", "Fix each error at the file and line the finding names, then build again.", false),
        new(9, "refused-by-name", "Refused by name: a named environment, an unregistered copy or a lock; the refusal's code is printed.", "Do what the finding's remedy says.", true),
        new(130, "interrupted", "Interrupted (Ctrl-C or --timeout); the cleanup ran and the state is as before.", "Run the verb again.", false),
    ];

    /// <summary>
    /// The exit an error takes, by its category, the word before the first dot of its code. The kernel, io and the cli name what went
    /// wrong (name.blank, sdk.missing, build.failed), and this switch alone says how estate exits for it: one arm per member of the
    /// kernel's closed ErrorCategory and no discard arm, so a member added there without an arm here fails the build. A name, an element,
    /// a fingerprint or a change the kernel rejects while reading a package or a database is input that could not be parsed (exit 2), as
    /// is a package, a refactorlog, a model two of whose objects share a key, or the copy registry. The posture, a profile, a reference,
    /// a connection, a SQLCMD value and the toolchain ledger are configuration (exit 6), whether io or the kernel finds the error, and
    /// so are a failure DacFx reports with no SQL Server error inside it, such as a package whose target platform the server is not, a
    /// verb this build lacks, and a defect in estate itself. A target of no known form is a bad argument, as is a flag or a verb estate
    /// does not know; a server that does not answer or refuses the identity is exit 4; a copy the registry does not hold, a scratch
    /// server on a named host, and an aggregate query the allowlist refuses are refused by name.
    /// </summary>
    // CS8524 (an enum value no member names) is disabled for this switch alone; CS8509, a named member without an arm, stays an error.
#pragma warning disable CS8524
    public static int ExitByCategory(ErrorCategory category) => category switch
    {
        ErrorCategory.Arguments => 1,
        ErrorCategory.Ref => 1,
        ErrorCategory.Target => 1,
        ErrorCategory.Name => 2,
        ErrorCategory.Element => 2,
        ErrorCategory.Fingerprint => 2,
        ErrorCategory.Package => 2,
        ErrorCategory.Refactorlog => 2,
        ErrorCategory.Model => 2,
        ErrorCategory.Registry => 2,
        ErrorCategory.Change => 2,
        ErrorCategory.Origin => 4,
        ErrorCategory.Server => 4,
        ErrorCategory.ScratchServer => 4,
        ErrorCategory.Git => 6,
        ErrorCategory.Sdk => 6,
        ErrorCategory.Tool => 6,
        ErrorCategory.Posture => 6,
        ErrorCategory.Profile => 6,
        ErrorCategory.Reference => 6,
        ErrorCategory.Connection => 6,
        ErrorCategory.Sqlcmd => 6,
        ErrorCategory.SyntheticCopy => 6,
        ErrorCategory.Toolchain => 6,
        ErrorCategory.DacFx => 6,
        ErrorCategory.Plan => 6,
        ErrorCategory.File => 6,
        ErrorCategory.Verb => 6,
        ErrorCategory.Internal => 6,
        ErrorCategory.Build => 7,
        ErrorCategory.Lock => 9,
        ErrorCategory.GitBranch => 9,
        ErrorCategory.Copy => 9,
        ErrorCategory.AggregateQuery => 9,
    };
#pragma warning restore CS8524

    /// <summary>The remedy a defect in estate itself carries: the defect is estate's to fix, and the envelope is what its maintainers need.</summary>
    private const string ReportIt = "Report this envelope and the command that produced it to estate's maintainers.";

    /// <summary>The exit an error takes: its category's arm of <see cref="ExitByCategory"/>.</summary>
    public static int Exit(Error error) => ExitByCategory(error.Category);

    /// <summary>An answer; with no stamp it stands on the tool alone, nothing here having loaded DacFx or reached SQL Server.</summary>
    public static Envelope Answer(string schema, Outcome outcome, int exit, string message, IReadOnlyList<Finding> findings, Stamp? stamp = null, Provenance? provenance = null,
        JsonObject? content = null, IReadOnlyList<string>? lines = null) =>
        new(schema, outcome, exit, message, findings, null, stamp, provenance, content, lines);

    /// <summary>An error as a verb's answer: its message the answer's, one finding of severity error carrying its code and remedy, and the exit of its category.</summary>
    public static Envelope Failed(Verb verb, Error error, Stamp? stamp = null) => Failed(verb.Output, "estate " + verb.Name, error, stamp);

    /// <summary>An error as an answer under <paramref name="schema"/>: the outcome its exit's name, its message the answer's, and one finding of severity error with <paramref name="subject"/>.</summary>
    public static Envelope Failed(string schema, string subject, Error error, Stamp? stamp = null)
    {
        var exit = Exits.Single(e => e.Code == Exit(error));
        return Answer(schema, Outcome.Of(exit), exit.Code, error.Message, [Finding.Of(error, subject)], stamp);
    }

    /// <summary>
    /// An exception no verb expected, as an answer: the error internal.unexpected, naming the exception's type, at its category's exit. Its
    /// message is kept only when <paramref name="withheld"/> is false: once a command has read a named environment's connection or other
    /// reference, an exception's message can quote what that environment holds (VALUES.md X2). <paramref name="word"/> is the verb as
    /// typed, empty when the exception came before the arguments were read.
    /// </summary>
    public static Envelope Unexpected(Verb? verb, string word, Exception exception, bool withheld)
    {
        var command = word.Length == 0 ? "estate" : "estate " + word;
        var what = command + " stopped on an unexpected " + exception.GetType().Name;
        var said = withheld ? "; its message is withheld, since the command read a named environment's connection and the message can quote what that environment holds." : ": " + exception.Message;
        return Failed(verb?.Output ?? "estate.envelope/1", command, new Error("internal.unexpected", what + said, ReportIt));
    }

    /// <summary>A verb the contract names and this build has no body for: the error verb.not-built, at its category's exit, naming the verb and nothing of when it arrives.</summary>
    public static Envelope NotBuilt(Verb verb) => Failed(verb, new Error("verb.not-built",
        "estate " + verb.Name + " is not in this build.", "Run estate --help to see the verbs this build has."));

    /// <summary>A word that names no verb: the error arguments.unknown-verb, at its category's exit, under the generic envelope.</summary>
    public static Envelope UnknownVerb(string word) => Failed("estate.envelope/1", "estate " + word, new Error("arguments.unknown-verb",
        "'" + word + "' is not a verb of estate: the verb table has no row of that name.", "Run estate --help to see the verbs."));

    /// <summary>
    /// The flags a verb reads: each --name followed by its value, or standing alone when it is a switch. A word outside a flag, a flag the
    /// verb does not take, a flag given twice, a value missing, and a required flag absent are each a bad argument (exit 1).
    /// </summary>
    public static Result<IReadOnlyDictionary<string, string>> Flags(IReadOnlyList<string> words, string[] required, string[] optional, string[] switches)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var valued = required.Contains(word) || optional.Contains(word);
            if (!valued && !switches.Contains(word) || flags.ContainsKey(word) || valued && (i + 1 == words.Count || words[i + 1].StartsWith("--", StringComparison.Ordinal)))
            {
                return new Error("arguments.unknown-flag", "'" + word + "' is " + (flags.ContainsKey(word) ? "given twice" : valued ? "a flag without its value" : "no flag this verb takes") + ".",
                    "Run estate --help to see each verb's flags.");
            }

            flags[word] = valued ? words[++i] : "";
        }

        return required.FirstOrDefault(r => !flags.ContainsKey(r)) is { } missing
            ? new Error("arguments.missing-flag", "The verb needs " + missing + ".", "Run estate --help to see each verb's flags.")
            : Result.Ok<IReadOnlyDictionary<string, string>>(flags);
    }
}
