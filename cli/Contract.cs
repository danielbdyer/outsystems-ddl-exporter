using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Estate.Kernel;

namespace Estate.Cli;

/// <summary>A row of the verb table: the verb, the question it answers, the milestone it arrives in, its body once built, and the schema of what it adds to the envelope.</summary>
public sealed record Verb(string Name, string Summary, int Arrives, Func<Checkout, IReadOnlyList<string>, Envelope>? Body = null, JsonObject? Content = null)
{
    /// <summary>The schema its --json answer names, such as estate.doctor/1.</summary>
    public string Output => "estate." + Name.TrimStart('-') + "/1";

    /// <summary>built; stub, a body ahead of its milestone; pending, no body yet.</summary>
    public string Status => Body is null ? "pending" : Arrives > Contract.Milestone ? "stub" : "built";
}

/// <summary>Where estate runs: the estate's root (Profiles.Root of the working directory), the working directory, and the tool folder ESTATE_TOOL names, if any.</summary>
public sealed record Checkout(string Root, string WorkingDirectory, string? Tool)
{
    public static Checkout Here() => new(Io.Profiles.Root(Directory.GetCurrentDirectory()), Directory.GetCurrentDirectory(), Environment.GetEnvironmentVariable("ESTATE_TOOL"));
}

/// <summary>A row of the frozen exit table: codes are added, never removed or renumbered (cli/exits.frozen).</summary>
public sealed record ExitCode(int Code, string Name, string Meaning, string Remedy, bool RemedyRequired);

/// <summary>The engine an answer stands on (R13), and the pin the toolchain ledger gives it; the pin is null where no ledger was read.</summary>
public sealed record Stamp(Engine Engine, Pin? Pin);

/// <summary>What every verb writes with --json: the kernel's engine as stamped, its receipt when it claims anything, the verdict, the findings, the exit, and what the verb adds (its Content).</summary>
public sealed record Envelope(string Schema, Stamp? Stamp, Receipt? Receipt, Verdict Verdict, IReadOnlyList<Finding> Findings, int Exit, JsonObject? Content = null);

/// <summary>The answer in a line; at exit 3 its kind says how the data blocked, and no other verdict has one.</summary>
public sealed record Verdict(string Outcome, string Message, Blocked? Kind = null);

/// <summary>How the data blocked (§4 row 15): the data-loss check (BlockOnPossibleDataLoss) stopped the publish because the table has rows, or SQL Server refused the change on existing rows (Msg 547, Msg 2628).</summary>
public enum Blocked { DataLossCheck, Violation }

/// <summary>The contract as data: the verb table and the exit table. --help --json and cli/schemas/ are generated from them.</summary>
public static class Contract
{
    /// <summary>The milestone this build completes.</summary>
    public const int Milestone = 0;

    private static readonly string[] Milestones = ["Foundation", "Read", "Predict", "Synthetic copy", "Prove", "Describe and gate", "After deploy", "Agent instructions"];

    public static string Version { get; } = typeof(Contract).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    public static readonly IReadOnlyList<Verb> Verbs =
    [
        new("doctor", "Can this machine do the work: the SDK and runtime, the tool and its DacFx against the toolchain ledger, the build route, the scratch server, Git LFS. estate doctor",
            1, Cli.Verbs.Doctor, Cli.Verbs.DoctorContent),
        new("read", "What a schema is, from a ref, a package or a database, read whole, with its fingerprint. estate read --from <target> [--project <path>]",
            1, Cli.Verbs.Read, Cli.Verbs.ReadContent),
        new("diff", "What changes between two schemas, property by property, deploy scripts and refactorlog included. estate diff --from <target> --to <target> [--project <path>] [--fail-on-change]",
            1, Cli.Verbs.Diff, Cli.Verbs.DiffContent),
        new("classify", "Which operation a change is, provisionally, from the committed evidence.", 2),
        new("predict", "Whether a change blocks or applies on each environment the caller can read, and why.", 2),
        new("measure", "What an environment's data looks like, as the evidence the synthetic copy is generated from.", 3),
        new("synthetic-copy", "A copy on the scratch server, built from the repository at a ref and filled with rows generated from the measured data.", 3),
        new("prove", "What the engine does with a change on a fresh copy, with a receipt.", 4),
        new("describe", "The pull request description, rendered from the receipts.", 5),
        new("gate", "The pull request's proof, reproduced from the clone.", 5),
        new("check", "Whether a database has drifted from the repository at a ref; the platform, the evidence and the locks arrive later. estate check drift --target <target> --at <ref> "
            + "[--profile <path>] [--project <path>]", 1, Cli.Verbs.Check, Cli.Verbs.CheckContent),
        new("knowledge", "The knowledge tree, packaged for each agent and vendored to the estate.", 5),
        new("--version", "The tool's version.", 0, (_, _) => Answer("estate.version/1", "done", "estate " + Version, [], 0)),
    ];

    public static readonly IReadOnlyList<ExitCode> Exits =
    [
        new(0, "done", "Done: the verb did its work.", "nothing to do", false),
        new(1, "bad-arguments", "Bad arguments: an unknown verb, flag or value.", "estate --help", false),
        new(2, "unparsed-input", "An input could not be parsed: a schema, a configuration file or a project.", "the file and line the finding names", true),
        new(3, "blocked", "Blocked by the data, a finding and not a failure: kind guard, the data-loss check stopped the publish because the table has rows; or kind violation, SQL Server refused the change on existing rows (Msg 547, Msg 2628).", "the site the finding names: the operation's two-release shape, or the rows it counts", false),
        new(4, "unreachable", "The target is unreachable: no scratch server, or SQL Server, Docker or LocalDB not answering.", "estate doctor; estate synthetic-copy up", true),
        new(5, "differs", "Divergence found: the target differs from the repository; the findings name each differing object.", "the objects the findings name", false),
        new(6, "configuration-refused", "The environment or configuration is refused: the .NET SDK missing, an unknown key, a literal credential, an engine outside the pinned window, a verb this build does not have yet, a failure DacFx reports with no SQL Server error inside (dacfx.failed), or a defect in estate itself (internal.unexpected).", "estate doctor, or the file or milestone the finding names", true),
        new(7, "build-failed", "The build failed; the findings carry the build's errors.", "the file and error the finding names", false),
        new(9, "refused-by-name", "Refused by name: a named environment, an unregistered copy or a lock; the refusal's code is printed.", "the refusal's own remedy", true),
        new(130, "interrupted", "Interrupted (Ctrl-C or --timeout); the cleanup ran and the state is as before.", "run the verb again", false),
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
        ErrorCategory.Engine => 6,
        ErrorCategory.Toolchain => 6,
        ErrorCategory.DacFx => 6,
        ErrorCategory.Verb => 6,
        ErrorCategory.Internal => 6,
        ErrorCategory.Build => 7,
        ErrorCategory.GitBranch => 9,
        ErrorCategory.Copy => 9,
        ErrorCategory.AggregateQuery => 9,
#pragma warning restore CS8524
    };

    /// <summary>The remedy a defect in estate itself carries: the defect is estate's to fix, and the envelope is what its maintainers need.</summary>
    private const string ReportIt = "Report this envelope and the command that produced it to estate's maintainers.";

    /// <summary>The exit an error takes: its category's arm of <see cref="ExitByCategory"/>.</summary>
    public static int Exit(Error error) => ExitByCategory(error.Category);

    /// <summary>A milestone as the plan writes it, M2; and as a person reads it, M2 (Predict).</summary>
    public static string M(int milestone) => "M" + milestone.ToString(CultureInfo.InvariantCulture);

    public static string Title(int milestone) => M(milestone) + " (" + Milestones[milestone] + ")";

    /// <summary>An answer; with no stamp it stands on the tool alone, nothing here having loaded DacFx or reached SQL Server.</summary>
    public static Envelope Answer(string schema, string outcome, string message, IReadOnlyList<Finding> findings, int exit, Stamp? stamp = null, Receipt? receipt = null, JsonObject? content = null) =>
        new(schema, stamp, receipt, new Verdict(outcome, message), findings, exit, content);

    /// <summary>An error as a verb's answer: its message the verdict, one finding of severity error carrying its code and remedy, and the exit of its category.</summary>
    public static Envelope Failed(Verb verb, Error error, Stamp? stamp = null) => Failed(verb.Output, "estate " + verb.Name, error, stamp);

    /// <summary>An error as an answer under <paramref name="schema"/>: the outcome its exit's name, its message the verdict, and one finding of severity error with <paramref name="subject"/>.</summary>
    public static Envelope Failed(string schema, string subject, Error error, Stamp? stamp = null) =>
        Answer(schema, Exits.Single(e => e.Code == Exit(error)).Name, error.Message, [Finding.Of(error, subject)], Exit(error), stamp);

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

    /// <summary>A verb the contract names and this build has no body for: the error verb.not-built, at its category's exit.</summary>
    public static Envelope NotBuilt(Verb verb) => Failed(verb, new Error("verb.not-built",
        "estate " + verb.Name + " is in the contract; its body arrives in " + Title(verb.Arrives) + ".", "estate --help lists what this build runs"));

    /// <summary>A word that names no verb: the error arguments.unknown-verb, at its category's exit, under the generic envelope.</summary>
    public static Envelope UnknownVerb(string word) => Failed("estate.envelope/1", "estate " + word, new Error("arguments.unknown-verb",
        "'" + word + "' is not a verb of estate: the verb table has no row of that name.", "estate --help"));

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
                    "estate --help names each verb's flags");
            }

            flags[word] = valued ? words[++i] : "";
        }

        return required.FirstOrDefault(r => !flags.ContainsKey(r)) is { } missing
            ? new Error("arguments.missing-flag", "The verb needs " + missing + ".", "estate --help names each verb's flags")
            : Result.Ok<IReadOnlyDictionary<string, string>>(flags);
    }
}
