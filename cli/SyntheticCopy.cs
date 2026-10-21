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
    /// What synthetic-copy adds to the envelope. up's syntheticCopy: the copy's name; the ref and its commit, and the change's commit where
    /// an existing data was planted; whether the base was restored from the copy cache; each table's rows planted and skipped; each table
    /// that held a row as restored or took one, its rows and content hash as restored and after; and the copy's own answer to each of the
    /// change's preconditions, in the existing data's form. check's checked: the copy, the ref and the commits; each table's rows and
    /// hash beside the hash up recorded; each foreign key's state and its rows whose parent row is absent; and the copy's answers. status's
    /// and down's copies: each synthetic copy this checkout's registry holds, the server it was made on, whether its build ended, the
    /// fingerprints its row records, whether its name's digits begin them, whether the local server holds it, and whether down dropped it.
    /// The lists can be long.
    /// </summary>
    public static JsonObject SyntheticCopyContent => new()
    {
        ["syntheticCopy"] = Render.Record(new()
        {
            ["copy"] = CopyPattern(), ["at"] = Render.Text(),
            ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"), ["head"] = Render.Nullable(Render.Pattern("^[0-9a-f]{40,64}$")),
            ["restored"] = new JsonObject { ["type"] = "boolean" },
            ["planted"] = Render.Long(Render.Record(new() { ["table"] = Render.Text(), ["planted"] = Count(), ["skipped"] = Count() })),
            ["tables"] = Render.Long(Render.Record(new()
            {
                ["table"] = Render.Text(), ["rowsBefore"] = Count(), ["before"] = Render.Nullable(Render.Fingerprint()), ["rows"] = Count(),
                ["hash"] = Render.Fingerprint(),
            })),
            ["answers"] = Render.Long(AnswerSchema()),
        }),
        ["checked"] = Render.Record(new()
        {
            ["copy"] = CopyPattern(), ["at"] = Render.Text(),
            ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"), ["head"] = Render.Nullable(Render.Pattern("^[0-9a-f]{40,64}$")),
            ["tables"] = Render.Long(Render.Record(new()
            {
                ["table"] = Render.Text(), ["rows"] = Count(), ["recorded"] = Render.Nullable(Render.Fingerprint()),
                ["hash"] = Render.Nullable(Render.Fingerprint()),
            })),
            ["references"] = Render.Long(Render.Record(new()
            {
                ["reference"] = Render.Text(), ["state"] = Render.Enum(["trusted", "untrusted", "disabled"]), ["orphans"] = Count(),
            })),
            ["answers"] = Render.Long(AnswerSchema()),
        }),
        ["copies"] = Render.Long(Render.Record(new()
        {
            ["copy"] = CopyPattern(), ["server"] = Render.Text(), ["ready"] = new JsonObject { ["type"] = "boolean" },
            ["base"] = Render.Nullable(Render.Fingerprint()), ["existingData"] = Render.Nullable(Render.Fingerprint()),
            ["verified"] = new JsonObject { ["type"] = "boolean" },
            ["present"] = Render.Nullable(new JsonObject { ["type"] = "boolean" }), ["dropped"] = new JsonObject { ["type"] = "boolean" },
        })),
    };

    private static JsonObject CopyPattern() => Render.Pattern("^dbchange_synthetic_[0-9a-f]{16}_existing-data(_[0-9a-f]{16})?$");

    /// <summary>A precondition's state as the existing data's file writes it.</summary>
    private static JsonObject AnswerSchema() => Render.Record(new()
    {
        ["precondition"] = new JsonObject
        {
            ["type"] = "object", ["required"] = new JsonArray("kind", "table", "columns"),
            ["properties"] = new JsonObject
            {
                ["kind"] = Render.Enum(["TableEmpty", "NotNull", "Fits", "ForeignKey", "Unique", "Check"]),
                ["table"] = Render.Text(), ["columns"] = Render.List(Render.Text()),
            },
        },
        ["populated"] = new JsonObject { ["type"] = "boolean" }, ["violating"] = new JsonObject { ["type"] = "boolean" },
        ["classes"] = Render.List(Render.Text()),
    });

    /// <summary>
    /// dbchange synthetic-copy up|check [--at &lt;ref&gt;] [--existing-data &lt;file&gt;] [--profile &lt;path&gt;]
    /// [--project &lt;path&gt;]; dbchange synthetic-copy status; dbchange synthetic-copy down [--if-idle].
    /// </summary>
    public static Envelope SyntheticCopy(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words) => words switch
    {
        ["up", ..] => Up(here, log, [.. words.Skip(1)], check: false),
        ["check", ..] => Up(here, log, [.. words.Skip(1)], check: true),
        ["status", ..] => Contract.Flags([.. words.Skip(1)], [], [], []).Bind(_ => Io.SyntheticCopy.Status(here, log))
            .Match(listing => Listed(listing, down: false), error => Contract.Failed(Of("synthetic-copy"), error)),
        ["down", ..] => Contract.Flags([.. words.Skip(1)], [], [], ["--if-idle"])
            .Bind(flags => Io.SyntheticCopy.Down(here, flags.ContainsKey("--if-idle"), log))
            .Match(listing => Listed(listing, down: true), error => Contract.Failed(Of("synthetic-copy"), error)),
        _ => Contract.Failed(Of("synthetic-copy"), new Error(
            "arguments.unknown-subcommand",
            "dbchange synthetic-copy needs what to do with the copy: up, check, status or down.",
            "Run dbchange synthetic-copy up --at <ref>.")),
    };

    /// <summary>
    /// synthetic-copy up's or check's arguments read, io/SyntheticCopy run, and its answer rendered: --at takes ref:&lt;ref&gt; or a ref
    /// alone, HEAD where it is absent; --existing-data a file by its path from the working directory.
    /// </summary>
    private static Envelope Up(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words, bool check)
    {
        var verb = Of("synthetic-copy");
        if (Contract.Flags(words, [], ["--at", "--existing-data", "--profile", "--project"], [])
            .Bind(flags => GitRef.Of("--at", flags.GetValueOrDefault("--at", "HEAD") is var at
                    && at.StartsWith("ref:", StringComparison.Ordinal) ? at[4..] : at)
                .Map(at => new Io.SyntheticCopy.Request(at, flags.GetValueOrDefault("--existing-data"),
                    flags.GetValueOrDefault("--profile"), flags.GetValueOrDefault("--project"))))
            .Failed(out var request, out var error))
        {
            return Contract.Failed(verb, error, Standing.Committed);
        }

        if (check)
        {
            var checking = Io.SyntheticCopy.Check(here, request, log);
            return checking.Result.Failed(out var @checked, out error) ? Contract.Failed(verb, error, checking.Stamp)
                : Checked(@checked, checking.Stamp!);
        }

        var up = Io.SyntheticCopy.Up(here, request, log);
        return up.Result.Failed(out var answer, out error) ? Contract.Failed(verb, error, up.Stamp) : Copied(answer, up.Stamp!);
    }

    /// <summary>
    /// synthetic-copy up's answer: built or current at exit 0, with the notes; needs-input at exit 8, each finding naming the agent's next
    /// step; differs at exit 5, a warning per operation the plan from the base to the copy holds, with the notes.
    /// </summary>
    internal static Envelope Copied(Io.SyntheticCopy.Answer answer, Stamp stamp)
    {
        var verb = Of("synthetic-copy");
        var at = "ref:" + answer.At + " (commit " + answer.Commit[..8] + ")";
        var content = new JsonObject
        {
            ["syntheticCopy"] = new JsonObject
            {
                ["copy"] = answer.Copy.ToString(), ["at"] = "ref:" + answer.At, ["commit"] = answer.Commit, ["head"] = answer.Head,
                ["restored"] = answer.Restored,
                ["planted"] = Render.Array(answer.Planted.Select(p => new JsonObject
                {
                    ["table"] = p.Table.ToString(), ["planted"] = p.Planted, ["skipped"] = p.Skipped,
                })),
                ["tables"] = Render.Array(answer.Hashes.Select(h => new JsonObject
                {
                    ["table"] = h.Table.ToString(), ["rowsBefore"] = h.RowsBefore,
                    ["before"] = h.Before is { } before ? Render.Digest(before) : null, ["rows"] = h.Rows,
                    ["hash"] = Render.Digest(h.After),
                })),
                ["answers"] = Render.Array(answer.Answers.Select(ExistingDataFile.StateJson)),
            },
        };
        var planted = answer.Head is { } head ? ", planted at the existing data for the change at commit " + head[..8]
            : ", with presence rows";
        return answer.Kind switch
        {
            Io.SyntheticCopy.Kind.Built => Contract.Answer(verb.Output, verb.Outcome("built"), 0,
                "The synthetic copy " + answer.Copy + " is built at " + at + planted + ".", [.. answer.Notes], stamp, content: content),
            Io.SyntheticCopy.Kind.Current => Contract.Answer(verb.Output, verb.Outcome("current"), 0,
                "The synthetic copy " + answer.Copy + " is built at " + at + " from these inputs already; nothing was applied.",
                [.. answer.Notes], stamp, content: content),
            Io.SyntheticCopy.Kind.NeedsInput => Contract.Answer(verb.Output, verb.Outcome("needs-input"), 8,
                "The synthetic copy " + answer.Copy + " at " + at + " needs input: " + Counted(answer.Findings.Count, "finding")
                    + " name" + (answer.Findings.Count == 1 ? "s" : "") + " the next step.",
                [.. answer.Findings], stamp, content: content),
            _ => Contract.Answer(verb.Output, verb.Outcome("differs"), 5,
                "The synthetic copy " + answer.Copy + " differs from " + at + ": the plan from the base to it holds "
                    + Counted(answer.Findings.Count, "difference") + ".",
                [.. answer.Findings, .. answer.Notes], stamp, content: content),
        };
    }

    /// <summary>
    /// synthetic-copy check's answer: passed at exit 0, with the notes; differs at exit 5, a finding per table whose rows are not the ones
    /// up planted, per orphan law 11′ refuses, per precondition the copy answers otherwise than the existing data, and per operation the
    /// plan from the base to the copy holds.
    /// </summary>
    internal static Envelope Checked(Io.SyntheticCopy.Checked answer, Stamp stamp)
    {
        var verb = Of("synthetic-copy");
        var at = "ref:" + answer.At + " (commit " + answer.Commit[..8] + ")";
        var content = new JsonObject
        {
            ["checked"] = new JsonObject
            {
                ["copy"] = answer.Copy.ToString(), ["at"] = "ref:" + answer.At, ["commit"] = answer.Commit, ["head"] = answer.Head,
                ["tables"] = Render.Array(answer.Tables.Select(t => new JsonObject
                {
                    ["table"] = t.Table.ToString(), ["rows"] = t.Rows,
                    ["recorded"] = t.Recorded is { } recorded ? Render.Digest(recorded) : null,
                    ["hash"] = t.Hash is { } hash ? Render.Digest(hash) : null,
                })),
                ["references"] = Render.Array(answer.References.Select(r => new JsonObject
                {
                    ["reference"] = r.State.Constraint.ToString(),
                    ["state"] = r.State.Disabled ? "disabled" : r.State.Untrusted ? "untrusted" : "trusted", ["orphans"] = r.Orphans,
                })),
                ["answers"] = Render.Array(answer.Answers.Select(ExistingDataFile.StateJson)),
            },
        };
        return answer.Passed
            ? Contract.Answer(verb.Output, verb.Outcome("passed"), 0, "The synthetic copy " + answer.Copy + " at " + at
                + " holds the rows up planted, each trusted reference resolves, it answers each precondition as its existing data does,"
                + " and the plan from the base to it is empty.", [.. answer.Notes], stamp, content: content)
            : Contract.Answer(verb.Output, verb.Outcome("differs"), 5, "The synthetic copy " + answer.Copy + " at " + at
                + " fails check: " + Counted(answer.Findings.Count, "finding") + " name" + (answer.Findings.Count == 1 ? "s" : "")
                + " what differs.", [.. answer.Findings, .. answer.Notes], stamp, content: content);
    }

    /// <summary>
    /// synthetic-copy status's and down's answer. status: listed at exit 0, or differs at exit 5 where a copy's name carries fingerprints
    /// its row does not record. down: dropped at exit 0, naming the copies dropped; busy at exit 0 where --if-idle met another dbchange
    /// process holding the lock, and nothing was dropped.
    /// </summary>
    internal static Envelope Listed(Io.SyntheticCopy.Listing listing, bool down)
    {
        var verb = Of("synthetic-copy");
        var content = new JsonObject
        {
            ["copies"] = Render.Array(listing.Copies.Select(c => new JsonObject
            {
                ["copy"] = c.Copy.ToString(), ["server"] = c.Server, ["ready"] = c.Ready,
                ["base"] = c.Base is { } b ? Render.Digest(b) : null, ["existingData"] = c.ExistingData is { } d ? Render.Digest(d) : null,
                ["verified"] = c.Verified, ["present"] = c.Present, ["dropped"] = c.Dropped,
            })),
        };
        var dropped = listing.Copies.Where(c => c.Dropped).Select(c => c.Copy.ToString()).ToList();
        return (down, listing.Busy, listing.Findings.Count) switch
        {
            (true, true, _) => Contract.Answer(verb.Output, verb.Outcome("busy"), 0, "Another dbchange process holds "
                + LocalState.Name + "/synthetic-copy.lock, so synthetic-copy down --if-idle dropped nothing.", [.. listing.Notes],
                content: content),
            (true, false, _) => Contract.Answer(verb.Output, verb.Outcome("dropped"), 0, dropped.Count == 0
                    ? "This checkout's registry holds no synthetic copy of this machine on the local server; nothing was dropped."
                    : "Dropped " + Copies(dropped.Count) + ": " + string.Join(", ", dropped) + ".",
                [.. listing.Notes], content: content),
            (false, _, 0) => Contract.Answer(verb.Output, verb.Outcome("listed"), 0, "This checkout's registry holds "
                + Copies(listing.Copies.Count) + (listing.Copies.Count == 0 ? "." : ", each name verified against"
                    + " the fingerprints its row records."), [.. listing.Notes], content: content),
            _ => Contract.Answer(verb.Output, verb.Outcome("differs"), 5, Copies(listing.Findings.Count)
                + " of this checkout's registry carr" + (listing.Findings.Count == 1 ? "ies" : "y")
                + " fingerprints in its name that its row does not record.", [.. listing.Findings, .. listing.Notes], content: content),
        };
    }

    private static string Copies(int count) =>
        count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (count == 1 ? " synthetic copy" : " synthetic copies");
}
