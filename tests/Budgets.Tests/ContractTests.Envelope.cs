using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Cli;
using DbChange.Tests;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// The CLI contract's envelope: each rule of its schema as a minimal pair, and the answers the contract's own types cannot construct.
/// </summary>
public sealed partial class ContractTests
{
    /// <summary>
    /// Each rule of the envelope schema as a minimal pair over the answer dbchange writes to --version --json: two changes
    /// that differ only in what the rule governs, one the rule admits and one it refuses. A rule made vacuous fails its
    /// pair; the admitted half shows that the refusal is the rule's, not a broken answer's.
    /// </summary>
    public static TheoryData<string> EnvelopeRules => new(EnvelopePairs.Keys);

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(EnvelopeRules))]
    public void Each_envelope_rule_refuses_the_answer_that_breaks_it(string rule)
    {
        var (admitted, refused) = EnvelopePairs[rule];

        VerbAnswer.Valid("dbchange.envelope.1.schema.json", Answer(admitted));
        Assert.False(VerbAnswer.Evaluate("dbchange.envelope.1.schema.json", Answer(refused)).IsValid,
            "the envelope schema admits an answer that breaks: " + rule);
    }

    /// <summary>
    /// §4 row 15, through the contract's own types: an answer at exit 3 names what blocked it, BlockOnPossibleDataLoss or a constraint
    /// violation, written as block-on-possible-data-loss and constraint-violation; one at exit 3 naming nothing, or at another exit naming
    /// something, cannot be constructed, and the schema refuses the same answers written by hand.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(BlockedBy.BlockOnPossibleDataLoss, "block-on-possible-data-loss")]
    [InlineData(BlockedBy.ConstraintViolation, "constraint-violation")]
    public void A_blocking_answer_names_what_blocked_it_exactly_at_exit_3(BlockedBy blockedBy, string written)
    {
        var blocked = new Outcome("blocked", [3], "the data blocked the change");
        var answer = new Envelope("dbchange.prove/1", blocked, 3, "Msg 50000: rows were detected.", [], blockedBy);

        var json = Render.Json(answer);

        Assert.Equal(written, (string?)json["blockedBy"]);
        VerbAnswer.Valid("dbchange.envelope.1.schema.json", json);
        Assert.Throws<ArgumentException>("blockedBy", () =>
            new Envelope("dbchange.prove/1", blocked, 3, "Msg 50000: rows were detected.", []));
        Assert.Throws<ArgumentException>("blockedBy", () =>
            new Envelope("dbchange.version/1", new Outcome("done", [0], "done"), 0, "dbchange 3.0.0", [], blockedBy));
        json["blockedBy"] = null;
        Assert.False(VerbAnswer.Evaluate("dbchange.envelope.1.schema.json", json).IsValid, "the schema admits exit 3 naming nothing");
    }

    /// <summary>
    /// An outcome's word and its exit are one decision: check drift's in-sync is exit 0 and differs exit 5, and an answer that pairs them
    /// otherwise cannot be constructed.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_answer_whose_exit_its_outcome_does_not_list_cannot_be_constructed()
    {
        var check = Contract.Verbs.Single(v => v.Name == "check");

        Assert.Throws<ArgumentException>("exit", () =>
            new Envelope(check.Output, check.Outcome("in-sync"), 5, "env:dev is in sync with ref:main.", []));
        Assert.Equal(5, new Envelope(check.Output, check.Outcome("differs"), 5, "env:dev differs from ref:main.", []).Exit);
        Assert.Equal([0, 5], Contract.Verbs.Single(v => v.Name == "diff").Outcome("differs").Exits);
        Assert.Throws<InvalidOperationException>(() => check.Outcome("done"));
    }

    /// <summary>
    /// The envelope's rules, each named by the sentence it pins, as (admitted, refused). The exit codes, the outcome words and the
    /// field names are written out rather than read from the contract, so a rule narrowed there fails here.
    /// </summary>
    private static readonly Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)> EnvelopePairs = Pairs();

    private static Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)> Pairs()
    {
        var pairs = new Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)>(StringComparer.Ordinal)
        {
            ["exit is a code of the frozen table"] = (WithOutcome("build-failed", 7), a => a["exit"] = 8),
            ["outcome is a word of a verb's set or a failure exit's name"] = (WithOutcome("in-sync", 0), WithOutcome("converged", 0)),
            ["outcome ties to its exits: a failure's name at its exit alone"] =
                (WithOutcome("unreachable", 4), WithOutcome("unreachable", 6)),
            ["outcome ties to its exits: differs at 0 or 5 and at no other"] = (WithOutcome("differs", 5), WithOutcome("differs", 6)),
            ["outcome ties to its exits: in-sync at 0 alone"] = (WithOutcome("in-sync", 0), WithOutcome("in-sync", 5)),
            ["outcome ties to its exits: ready at 0 and degraded at 6"] = (WithOutcome("degraded", 6), WithOutcome("ready", 6)),
            ["the message is present"] = (a => a["message"] = "dbchange 3.0.0", a => a["message"] = ""),
            ["exit 3 names what blocked it"] = (WithBlocked("block-on-possible-data-loss"), WithBlocked(null)),
            ["what blocked it is BlockOnPossibleDataLoss or a constraint violation"] =
                (WithBlocked("constraint-violation"), WithBlocked("guard")),
            ["no other exit names what blocked it"] = (_ => { }, a => a["blockedBy"] = "block-on-possible-data-loss"),
            ["an answer says what blocked it, as null when the data did not block"] = (_ => { }, a => a.Remove("blockedBy")),
            ["a finding's severity is error, warning or note"] = (Finds(1, "note", remedy: null), Finds(1, "warn", remedy: null)),
            ["a finding of severity error carries a remedy"] = (Finds(1, "warning", remedy: null), Finds(1, "error", remedy: null)),
            ["a finding's code is in the one code pattern"] =
                (Finds(1, "note", code: "local-server.missing"), Finds(1, "note", code: "Local-Server.missing")),
            ["an answer that names its whole file was cut"] =
                (WithCut(true, ".dbchange/runs/20260925T101502Z-4242-0a1b/answer.json"),
                WithCut(false, ".dbchange/runs/20260925T101502Z-4242-0a1b/answer.json")),
            ["an answer that was not cut names no file"] = (WithCut(false, null), WithCut(true, ".dbchange/runs/x/queries.log")),
            ["the whole file is the run's answer.json"] =
                (WithCut(true, ".dbchange/runs/20260925T101502Z-4242-0a1b/answer.json"), WithCut(true, "answer.json")),
            ["the log is the run's queries.log"] =
                (a => a["log"] = ".dbchange/runs/20260925T101502Z-4242-0a1b/queries.log",
                a => a["log"] = ".dbchange/runs/20260925T101502Z-4242-0a1b/answer.json"),
            ["an answer names its log, as null when the run sent no statement"] = (a => a["log"] = null, a => a.Remove("log")),
            ["the time taken is a whole number of milliseconds, 0 or more"] = (a => a["elapsedMs"] = 1250, a => a["elapsedMs"] = -1),
            ["an answer names the time taken"] = (_ => { }, a => a.Remove("elapsedMs")),
            ["a provenance's at is a date-time"] = (WithProvenance(_ => { }), WithProvenance(p => p["at"] = "yesterday")),
            ["a provenance's existing data is null exactly when it lacks it"] =
                (WithProvenance(p => (p["existingData"], p["lacking"]) = ("sha256:" + new string('3', 64), new JsonArray())),
                WithProvenance(p => p["existingData"] = "sha256:" + new string('3', 64))),
            ["a provenance's server is null exactly when it lacks it"] =
                (WithProvenance(p => p["server"] = null, "existingData", "server"), WithProvenance(p => p["server"] = null)),
            ["a provenance lacks only an input it can lack"] = (WithProvenance(_ => { }), WithProvenance(_ => { }, "existingData", "seed")),
            ["the server names its edition"] = (WithProvenance(_ => { }), WithProvenance(p => p["server"]!["edition"] = "")),
            ["the server names no image"] = (WithProvenance(_ => { }),
                WithProvenance(p => p["server"]!["image"] = "sha256:" + new string('5', 64))),
            ["the server's version is SQL Server's product version"] =
                (WithStamp(s => s["server"]!["version"] = "15.0.4430.1"), WithStamp(s => s["server"]!["version"] = "SQL Server 2022")),
            ["the stamp's DacFx is a release version"] = (WithStamp(s => s["dacfx"] = "170.5.96.0"), WithStamp(s => s["dacfx"] = "latest")),
            ["the stamp's pin is UNPINNED or a release version"] =
                (WithStamp(s => s["pin"] = "170.5.96"), WithStamp(s => s["pin"] = "unpinned")),
        };

        // Instruction architecture §9.1: a remedy is required for severity error and for exits 2, 4, 6 and 9.
        foreach (var exit in (int[])[2, 4, 6, 9])
        {
            var code = exit.ToString(CultureInfo.InvariantCulture);
            pairs["exit " + code + " names a finding"] = (Finds(exit, "error", "dbchange doctor"), Finds(exit, severity: null));
            pairs["exit " + code + " gives every finding a remedy"] =
                (Finds(exit, "warning", "dbchange doctor"), Finds(exit, "warning", remedy: null));
        }

        // Milestones §3: a provenance names each input a claim stands on, the target and when, and what it lacks.
        foreach (var field in (string[])["change", "schema", "existingData", "dacfx", "server", "publishProfile", "target", "at",
            "lacking"])
        {
            pairs["a provenance carries its " + field] = (WithProvenance(_ => { }), WithProvenance(p => p.Remove(field)));
        }

        // The stamp: the tool's version, DacFx, the pin and the SQL Server, each null where the answer stands on the tool alone.
        foreach (var field in (string[])["version", "dacfx", "pin", "server"])
        {
            pairs["the stamp names its " + field] = (WithStamp(_ => { }), a => a.Remove(field));
        }

        // The envelope writes a fingerprint as sha256: and the digest's 64 lowercase hex digits, and nothing else.
        (string Name, string Text)[] malformed =
            [("63 digits", "sha256:" + new string('a', 63)), ("upper case", "sha256:" + new string('A', 64)),
            ("another algorithm", "md5:" + new string('a', 64)), ("no algorithm", new string('a', 64))];
        foreach (var field in (string[])["change", "schema", "publishProfile"])
        {
            foreach (var (name, text) in malformed)
            {
                pairs["a provenance's " + field + " is a sha256 fingerprint, not " + name] =
                    (WithProvenance(_ => { }), WithProvenance(p => p[field] = text));
            }
        }

        return pairs;
    }

    /// <summary>The answer dbchange writes to --version --json, then changed.</summary>
    private static JsonNode Answer(Action<JsonObject> change)
    {
        using var output = new MemoryStream();
        Cli.Program.Run(["--version", "--json"], output);
        var answer = JsonNode.Parse(output.ToArray())!.AsObject();
        change(answer);
        return answer;
    }

    /// <summary>
    /// An answer whose outcome reads as <paramref name="word"/> at <paramref name="exit"/>, with a finding carrying a remedy where the exit
    /// requires one.
    /// </summary>
    private static Action<JsonObject> WithOutcome(string word, int exit) => answer =>
    {
        Finds(exit, exit is 2 or 4 or 6 or 9 ? "error" : null, "dbchange doctor")(answer);
        answer["outcome"] = word;
    };

    /// <summary>Blocked by the data: exit 3, naming what blocked it, or naming nothing.</summary>
    private static Action<JsonObject> WithBlocked(string? blockedBy) => answer =>
    {
        answer["outcome"] = "blocked";
        answer["exit"] = 3;
        answer["blockedBy"] = blockedBy;
    };

    /// <summary>An answer cut or not, naming its whole file or none.</summary>
    private static Action<JsonObject> WithCut(bool truncated, string? full) => answer =>
    {
        answer["truncated"] = truncated;
        answer["full"] = full;
    };

    /// <summary>
    /// An answer at <paramref name="exit"/> with one finding of <paramref name="severity"/> and <paramref name="code"/>, or with none when
    /// the severity is null; the outcome follows the exit.
    /// </summary>
    private static Action<JsonObject> Finds(
        int exit, string? severity, string? remedy = null, string code = "block-on-possible-data-loss.rows-present") => answer =>
    {
        answer["exit"] = exit;
        answer["outcome"] = exit switch
        {
            0 => "done", 1 => "bad-arguments", 2 => "unparsed-input", 4 => "unreachable", 6 => "configuration-refused", 7 => "build-failed",
            9 => "refused-by-name", var other => (string?)answer["outcome"] ?? "done",
        };
        answer["findings"] = severity is null ? new JsonArray() : new JsonArray(new JsonObject
        {
            ["code"] = code, ["severity"] = severity, ["subject"] = "dbo.Customer.Email",
            ["message"] = "BlockOnPossibleDataLoss stopped the publish: the table has rows.", ["remedy"] = remedy,
        });
    };

    /// <summary>An answer carrying a whole stamp, a copy's server included, then changed.</summary>
    private static Action<JsonObject> WithStamp(Action<JsonObject> change) => answer =>
    {
        answer["version"] = "3.0.0+ffaf717c3121f54aab6ca115af89326343312aa7";
        answer["dacfx"] = "170.5.96";
        answer["pin"] = "UNPINNED";
        answer["server"] = Server();
        change(answer);
    };

    /// <summary>
    /// An answer carrying a well-formed drift claim on a copy (§3's inputs, the target and when), lacking its existing data, then changed;
    /// <paramref name="lacking"/> replaces what it lacks when any is given.
    /// </summary>
    private static Action<JsonObject> WithProvenance(Action<JsonObject> change, params string[] lacking) => answer =>
    {
        var provenance = new JsonObject
        {
            ["change"] = "sha256:" + new string('1', 64), ["schema"] = "sha256:" + new string('2', 64), ["existingData"] = null,
            ["dacfx"] = "170.5.96", ["server"] = Server(), ["publishProfile"] = "sha256:" + new string('4', 64),
            ["target"] = "copy:dbchange_host_4242_0a1b2c3d", ["at"] = "2026-09-25T10:15:44Z",
            ["lacking"] = new JsonArray([.. (lacking.Length == 0 ? ["existingData"] : lacking).Select(input => (JsonNode?)input)]),
        };
        change(provenance);
        answer["provenance"] = provenance;
    };

    private static JsonObject Server() =>
        new() { ["version"] = "16.0.4305.5", ["edition"] = "Express Edition (64-bit)", ["compatibilityLevel"] = 160 };
}
