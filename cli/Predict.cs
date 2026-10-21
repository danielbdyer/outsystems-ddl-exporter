using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

public static partial class Verbs
{
    /// <summary>
    /// What predict adds to the envelope: the head, its commit, the base's commit and the change's fingerprint; per environment, its
    /// outcome, what blocked it and how it ships, why it blocks, its counts, what was not answered, its baseline drift, its existing data,
    /// the claim's provenance, or the error that left it unread; and the Markdown block to paste into the pull request.
    /// </summary>
    public static JsonObject PredictContent => new()
    {
        ["predict"] = Render.Record(new()
        {
            ["at"] = Render.Text(), ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"), ["base"] = Render.Pattern("^[0-9a-f]{40,64}$"),
            ["change"] = Render.Fingerprint(),
            ["environments"] = Render.List(Render.Record(new()
            {
                ["environment"] = Render.Text(),
                ["outcome"] = Render.Enum(["applies", "blocks", "unpredicted", "unread"]),
                ["blockedBy"] = Render.Nullable(Render.Enum(["block-on-possible-data-loss", "constraint-violation"])),
                ["shipping"] = Render.Nullable(Render.Text()),
                ["because"] = Render.List(Render.Text()),
                ["counts"] = Render.Long(Render.Record(new()
                {
                    ["precondition"] = PreconditionSchema(), ["rows"] = Count(), ["violating"] = Count(),
                })),
                ["unanswered"] = Render.Long(Render.Record(new() { ["precondition"] = Render.Text(), ["why"] = Render.Text() })),
                ["baselineDrift"] = Render.Long(Render.Record(new()
                {
                    ["operation"] = Render.Pattern("^[a-z]+(-[a-z]+)*$"), ["key"] = Render.Text(),
                })),
                ["existingData"] = Render.Nullable(Render.Record(new()
                {
                    ["schema"] = new JsonObject { ["const"] = ExistingDataFile.Schema },
                    ["states"] = Render.Long(StateSchema()),
                    ["trust"] = Render.List(Render.Record(new()
                    {
                        ["constraint"] = Render.Text(), ["type"] = Render.Enum(["ForeignKeyConstraint", "CheckConstraint"]),
                        ["disabled"] = new JsonObject { ["type"] = "boolean" }, ["untrusted"] = new JsonObject { ["type"] = "boolean" },
                    })),
                })),
                ["provenance"] = Render.Nullable(Render.Defined("provenance")),
                ["error"] = Render.Nullable(Render.Record(new() { ["code"] = Render.Text(), ["message"] = Render.Text(),
                    ["remedy"] = Render.Text() })),
            })),
            ["block"] = Render.Text(),
        }),
    };

    /// <summary>A precondition as the existing data's file writes it: its kind, one of the thirteen, its table and its columns, and what
    /// its kind holds beside them.</summary>
    internal static JsonObject PreconditionSchema() => new()
    {
        ["type"] = "object", ["required"] = new JsonArray("kind", "table", "columns"),
        ["properties"] = new JsonObject
        {
            ["kind"] = Render.Enum(["TableEmpty", "NotNull", "Fits", "ForeignKey", "Unique", "Check", "KeyLength", "Rebuild", "Computed",
                "Temporal", "Scan", "Authored", "SeedDrift"]),
            ["table"] = Render.Text(), ["columns"] = Render.List(Render.Text()),
        },
    };

    /// <summary>A precondition's state as the existing data's file writes it.</summary>
    internal static JsonObject StateSchema() => Render.Record(new()
    {
        ["precondition"] = PreconditionSchema(),
        ["populated"] = new JsonObject { ["type"] = "boolean" }, ["violating"] = new JsonObject { ["type"] = "boolean" },
        ["classes"] = Render.List(Render.Text()),
    });

    /// <summary>
    /// dbchange predict [--env &lt;e&gt;[,&lt;e&gt;]] [--at &lt;ref&gt;] [--base &lt;ref&gt;] [--project &lt;path&gt;]: --env names the
    /// environments, dev where it is absent; --at takes ref:&lt;ref&gt; or a ref alone, HEAD where it is absent; --base the branch the
    /// change merges into, origin/HEAD or main where it is absent.
    /// </summary>
    public static Envelope Predict(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words)
    {
        var verb = Of("predict");
        if (Contract.Flags(words, [], ["--env", "--at", "--base", "--project"], [])
            .Bind(flags => (flags.TryGetValue("--env", out var named)
                    ? Result.All(named.Split(',').Select(n => EnvironmentName.Of("--env", n.Trim())))
                    : Result.Ok<IReadOnlyList<EnvironmentName>>([Io.Predict.Dev]))
                .Bind(environments => GitRef.Of("--at", Unprefixed(flags.GetValueOrDefault("--at", "HEAD")))
                .Bind(at => (flags.TryGetValue("--base", out var @base) ? GitRef.Of("--base", Unprefixed(@base)).Map(b => (GitRef?)b)
                        : Result.Ok<GitRef?>(null))
                    .Map(@base => new Io.Predict.Request(environments, at, @base, flags.GetValueOrDefault("--project"))))))
            .Failed(out var request, out var error))
        {
            return Contract.Failed(verb, error, Standing.Committed);
        }

        var predicted = Io.Predict.Run(here, request, log);
        return predicted.Result.Failed(out var answer, out error) ? Contract.Failed(verb, error, predicted.Stamp)
            : Predicted(answer, predicted.Stamp!);
    }

    private static string Unprefixed(string reference) =>
        reference.StartsWith("ref:", StringComparison.Ordinal) ? reference[4..] : reference;

    /// <summary>The outcome an environment's prediction takes: blocks, applies, or unpredicted, where it is not whole.</summary>
    private static string Word(Io.Predict.Predicted p) =>
        p.Outcome is Kernel.Outcome.Blocks ? "blocks" : p.Whole ? "applies" : "unpredicted";

    /// <summary>
    /// predict's answer: blocks at exit 3 when an environment blocks, BlockOnPossibleDataLoss named as what blocked it when it stops any;
    /// unpredicted at exit 0 when none blocks and one is not predicted whole; applies at exit 0 when every environment read applies; and,
    /// when none could be read, the first one's error at its exit, each environment's error a finding. An environment that could not be
    /// read is a warning carrying its error, the sentence of a denied login among them, and the others are predicted beside it.
    /// </summary>
    internal static Envelope Predicted(Io.Predict.Answer answer, Stamp stamp)
    {
        var verb = Of("predict");
        var read = answer.Environments.Select(e => e.Predicted).OfType<Result<Io.Predict.Predicted>.Ok>().Select(r => r.Value).ToList();
        var failed = answer.Environments.Where(e => e.Predicted is Result<Io.Predict.Predicted>.Failed)
            .Select(e => (e.Name, Error: ((Result<Io.Predict.Predicted>.Failed)e.Predicted).Error)).ToList();
        if (read.Count == 0)
        {
            return Contract.Failed(verb, failed[0].Error, stamp) with
            {
                Findings = [.. failed.Select(f => Finding.Of(f.Error, "env:" + f.Name))],
            };
        }

        var block = string.Join('\n', PredictionBlock.Markdown(answer, stamp.DacFx)) + "\n";
        var blocking = read.Select(p => p.Outcome).OfType<Kernel.Outcome.Blocks>().ToList();
        var content = new JsonObject
        {
            ["predict"] = new JsonObject
            {
                ["at"] = "ref:" + answer.At, ["commit"] = answer.Commit, ["base"] = answer.Base, ["change"] = Render.Digest(answer.Change),
                ["environments"] = Render.Array(answer.Environments.Select(e => e.Predicted.Match(p => PredictedJson(e.Name, p),
                    error => UnreadJson(e.Name, error)))),
                ["block"] = block,
            },
        };
        var said = string.Join("; ", answer.Environments.Select(e => "env:" + e.Name + " " + e.Predicted.Match(
            p => Word(p) is "unpredicted" ? "is not predicted whole" : Word(p), _ => "is not read")));
        IReadOnlyList<Finding> findings =
        [
            .. failed.Select(f => Finding.Warning(f.Error.Code, "env:" + f.Name, f.Error.Message, f.Error.Remedy)),
            .. read.SelectMany(p => p.Notes).DistinctBy(n => (n.Code, n.Subject, n.Message)),
            .. answer.Notes,
            .. Stands(stamp),
        ];
        var (lines, whole) = (block.TrimEnd('\n').Split('\n'), read.All(p => p.Whole));
        return blocking.Count > 0
            ? new Envelope(verb.Output, verb.Outcome("blocks"), 3, "The change blocks: " + said + ".", findings,
                blocking.Any(b => b.ByBlockOnPossibleDataLoss) ? BlockedBy.BlockOnPossibleDataLoss : BlockedBy.ConstraintViolation, stamp,
                content: content, lines: lines)
            : Contract.Answer(verb.Output, verb.Outcome(whole ? "applies" : "unpredicted"), 0,
                "The change " + (whole ? "applies" : "is not predicted whole") + ": " + said + ".", findings, stamp, content: content,
                lines: lines);
    }

    /// <summary>One environment predicted, as the content writes it.</summary>
    private static JsonObject PredictedJson(EnvironmentName name, Io.Predict.Predicted p)
    {
        var blocks = p.Outcome as Kernel.Outcome.Blocks;
        return new()
        {
            ["environment"] = name.ToString(), ["outcome"] = Word(p),
            ["blockedBy"] = blocks is null ? null
                : blocks.ByBlockOnPossibleDataLoss ? "block-on-possible-data-loss" : "constraint-violation",
            ["shipping"] = blocks?.Shipping.Match(() => "two-releases", operation => operation, () => "prove-it"),
            ["because"] = Render.Array((blocks?.Because ?? default).Select(s => (JsonNode?)s.Precondition.ToString())),
            ["counts"] = Render.Array(p.Prediction.Counts.Select(ExistingDataFile.CountJson)),
            ["unanswered"] = Render.Array(p.Unanswered.Select(u => new JsonObject
            {
                ["precondition"] = u.Precondition.ToString(), ["why"] = u.Why,
            })),
            ["baselineDrift"] = Render.Array(p.Outcome.BaselineDrift.Select(o => new JsonObject
            {
                ["operation"] = o.Kind.Word, ["key"] = o.Key.ToString(),
            })),
            ["existingData"] = JsonNode.Parse(ExistingDataFile.Text(p.Prediction.ExistingData)),
            ["provenance"] = Render.Json(p.Prediction.Provenance),
            ["error"] = null,
        };
    }

    /// <summary>An environment that could not be read, as the content writes it.</summary>
    private static JsonObject UnreadJson(EnvironmentName name, Error error) => new()
    {
        ["environment"] = name.ToString(), ["outcome"] = "unread", ["blockedBy"] = null, ["shipping"] = null, ["because"] = new JsonArray(),
        ["counts"] = new JsonArray(), ["unanswered"] = new JsonArray(), ["baselineDrift"] = new JsonArray(), ["existingData"] = null,
        ["provenance"] = null,
        ["error"] = new JsonObject { ["code"] = error.Code, ["message"] = error.Message, ["remedy"] = error.Remedy },
    };

    /// <summary>What every claim of the run stands on that its findings say: UNPINNED while the ledger pins no DacFx release, and the
    /// profiles not verified against the Octopus step's (§17 item 15).</summary>
    private static IEnumerable<Finding> Stands(Stamp stamp) =>
    [
        .. stamp.Pin is Pin.Unpinned
            ? new[]
            {
                Finding.Note("toolchain.unpinned", Io.Doctor.Ledger, "This answer stands on DacFx " + stamp.DacFx + ", UNPINNED: "
                    + Io.Doctor.Ledger + " pins no DacFx release for dbchange " + Contract.Version.Split('+')[0] + "."),
            }
            : [],
        Finding.Note("profile.unverified", "the environments' publish profiles",
            "The SSDT repository commits no copy of the publish profile the Octopus step applies, so no environment's profile is verified "
                + "against it."),
    ];
}
