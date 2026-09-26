using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

public static partial class Verbs
{
    /// <summary>The checks the verb table names beyond drift, which this build does not have.</summary>
    private static readonly HashSet<string> Later = new(StringComparer.Ordinal) { "cdc", "evidence", "inflight", "outsystems", "environments" };

    /// <summary>
    /// What check adds to the envelope: the kind of check, the target, the ref and its commit, the counts, each operation of the deploy plan
    /// (its word, DacFx's name where dbchange's list lacks it, the element and the data issues it raises), and the columns that differ, as diff
    /// writes a change; the two lists can be long.
    /// </summary>
    public static JsonObject CheckContent => new()
    {
        ["check"] = Render.Record(new()
        {
            ["kind"] = Render.Enum(["drift"]), ["target"] = Render.Text(), ["at"] = Render.Text(), ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"),
            ["counts"] = Render.Record(new() { ["operations"] = Count(), ["columns"] = Count() }),
            ["operations"] = Render.Long(Render.Record(new()
            {
                ["operation"] = Render.Pattern("^[a-z]+(-[a-z]+)*$"), ["name"] = Render.Nullable(Render.Text()), ["key"] = Render.Text(),
                ["issues"] = Render.List(new JsonObject { ["type"] = "integer" }),
            })),
            ["columns"] = ChangeSchema(),
        }),
    };

    /// <summary>dbchange check drift --target &lt;target&gt; --at &lt;ref&gt; [--profile &lt;path&gt;] [--project &lt;path&gt;]; the other checks arrive later.</summary>
    public static Envelope Check(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words) => words switch
    {
        ["drift", ..] => Drift(here, log, [.. words.Skip(1)]),
        [var kind, ..] when Later.Contains(kind) => Contract.NotBuilt(Of("check") with { Name = "check " + kind }) with { Schema = Of("check").Output },
        _ => Contract.Failed(Of("check"), new Error("arguments.unknown-check", "dbchange check needs the check to run; this build runs check drift.", "Run dbchange check drift --target <target> --at <ref>.")),
    };

    /// <summary>check drift's arguments read, io/DriftCheck run, and its answer rendered; --at takes ref:&lt;ref&gt;, or a ref alone, since git forbids ':' in a ref's name.</summary>
    private static Envelope Drift(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words)
    {
        var verb = Of("check");
        if (Contract.Flags(words, ["--target", "--at"], ["--profile", "--project"], []).Bind(flags => SqlServer.Target(flags["--target"], "--target")
                .Bind(target => GitRef.Of("--at", flags["--at"].StartsWith("ref:", StringComparison.Ordinal) ? flags["--at"][4..] : flags["--at"])
                    .Map(at => new DriftCheck.Request(target, at, flags.GetValueOrDefault("--profile"), flags.GetValueOrDefault("--project")))))
            .Failed(out var request, out var error))
        {
            return Contract.Failed(verb, error, DacFx.Version.Match<Stamp?>(dacfx => new Stamp(dacfx), _ => null));
        }

        var drift = DriftCheck.Run(here, request, log);
        return drift.Result.Failed(out var answer, out error) ? Contract.Failed(verb, error, drift.Stamp) : Drifted(here, answer, drift.Stamp!);
    }

    /// <summary>
    /// check drift's answer: matches, exit 0, when the deploy plan is empty; else differs, exit 5, with a warning per operation that changes an
    /// object, one note listing what DacFx adds for the objects that depend on them, a warning per alert, and a warning per column that differs
    /// under a table the plan alters; each answer with the notes the profile, the package, the two models and the plan raised, UNPINNED
    /// while the ledger pins no DacFx release, and that the profile is not verified against the Octopus step's (§17 item 15).
    /// </summary>
    private static Envelope Drifted(Checkout here, DriftCheck.Answer answer, Stamp stamp)
    {
        var (verb, at, printer) = (Of("check"), "ref:" + answer.At, new Printer());
        var profile = Path.GetRelativePath(here.Root, answer.Profile).Replace('\\', '/');
        IReadOnlyList<Finding> standing =
        [
            .. answer.Notes,
            .. stamp.Pin is Pin.Unpinned ? new[] { Finding.Note("toolchain.unpinned", Io.Doctor.Ledger, "This answer stands on DacFx " + stamp.DacFx + ", UNPINNED: " + Io.Doctor.Ledger
                + " pins no DacFx release for dbchange " + Contract.Version.Split('+')[0] + ".") } : [],
            Finding.Note("profile.unverified", profile, "The SSDT repository commits no copy of the publish profile the Octopus step applies, so " + profile + " is not verified against it."),
        ];
        var (operations, columns) = answer.Drift.Match(_ => (default(SortedArray<PlanOperation>), new Change([], [], [], [])), differs => (differs.Plan.Operations, differs.Columns));
        var content = new JsonObject
        {
            ["check"] = new JsonObject
            {
                ["kind"] = "drift", ["target"] = answer.Target.ToString(), ["at"] = at, ["commit"] = answer.Commit,
                ["counts"] = new JsonObject { ["operations"] = operations.Count, ["columns"] = columns.Created.Count + columns.Dropped.Count + columns.Altered.Count },
                ["operations"] = Render.Array(operations.Select(o => new JsonObject
                {
                    ["operation"] = o.Kind.Word, ["name"] = o.Kind is PlanOperationKind.Unlisted ? o.Kind.Name : null, ["key"] = o.Key.ToString(),
                    ["issues"] = Render.Array(o.Issues.Select(i => (JsonNode?)i)),
                })),
                ["columns"] = Json(columns, printer),
            },
        };
        var commit = " (commit " + answer.Commit[..8] + ")";
        return answer.Drift.Match(
            _ => Contract.Answer(verb.Output, verb.Outcome("in-sync"), 0, answer.Target + " is in sync with " + at + commit + ".", standing, stamp, answer.Provenance, content),
            differs => Contract.Answer(verb.Output, verb.Outcome("differs"), 5,
                answer.Target + " differs from " + at + commit + ": the deploy plan holds " + Counted(differs.Plan.Operations.Count, "operation") + ".",
                [.. Differences(answer, differs, at), .. columns.CaseOnlyRenamed.Select(pair => CaseOnly("drift.case-only-rename", pair, answer.Collation)), .. printer.Findings, .. standing],
                stamp, answer.Provenance, content));
    }

    /// <summary>
    /// What a plan that differs says, object by object: a warning per operation that changes an object, naming it; one note listing the
    /// operations DacFx adds for the objects that depend on those (a refresh, an unbind, a rebind), none a difference of its own; a warning
    /// per alert, quoting DacFx's text, which names types and objects and never a row; and a warning per column line.
    /// </summary>
    private static IEnumerable<Finding> Differences(DriftCheck.Answer answer, Drift.Differs differs, string at)
    {
        var remedy = "Run dbchange diff --from " + answer.Target + " --to " + at + " to see each property that differs.";
        var consequences = differs.Plan.Operations.Where(o => o.Kind.IsConsequence).ToList();
        return differs.Plan.Operations.Where(o => !o.Kind.IsConsequence)
            .Select(o => Finding.Warning("drift." + o.Kind.Word, o.Key.ToString(), "The deploy plan against " + answer.Target + " would " + Verb(o.Kind) + " " + o.Key + ".", remedy))
            .Concat(consequences.Count == 0 ? [] : [Finding.Note("drift.consequence", "the deploy plan", "DacFx also plans, for the objects that depend on those it changes: "
                + string.Join(", ", consequences.Select(o => o.Kind.Name + " " + o.Key)) + "; none is a difference of its own.")])
            .Concat(differs.Plan.Alerts.Select(alert => alert.Kind switch
            {
                PlanAlertKind.DataIssue => Finding.Warning("drift.data-issue", differs.Plan.Operations.FirstOrDefault(o => alert.Id is { } id && o.Issues.Contains(id))?.Key.ToString() ?? "the deploy plan",
                    alert.Text, remedy),
                PlanAlertKind.DataMotion => Finding.Warning("drift.data-motion", alert.Text, "The deploy plan against " + answer.Target + " would copy the rows of " + alert.Text + " into a rebuilt table.", remedy),
                _ => Finding.Warning("drift." + alert.Kind.Word, "the deploy plan", "DacFx's " + alert.Kind.Name + " alert: " + alert.Text, remedy),
            }))
            .Concat(Lines(differs.Columns).Select(line => line.Split(": ", 2) is [var key, var change]
                ? Finding.Warning("drift.column", key, change + ", from the target to the repository.") : Finding.Warning("drift.column", line, line + ".")));
    }

    /// <summary>What an operation does, as the message's verb: create, alter, drop, rebuild, rename; for a name dbchange's list lacks, DacFx's operation by its name.</summary>
    private static string Verb(PlanOperationKind kind) => kind switch
    {
        PlanOperationKind.TableRebuild => "rebuild",
        PlanOperationKind.Unlisted unlisted => "run DacFx's " + unlisted.Name + " operation on",
        _ => kind.Word,
    };

    private static string Counted(int count, string noun) => count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? "" : "s");
}
