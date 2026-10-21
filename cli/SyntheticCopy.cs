using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

public static partial class Verbs
{
    /// <summary>The subcommands the verb table names beyond up, which this build does not have.</summary>
    private static readonly HashSet<string> LaterCopy = new(StringComparer.Ordinal) { "check", "status", "down" };

    /// <summary>
    /// What synthetic-copy up adds to the envelope: the copy's name; the ref and its commit, and the change's commit where an existing data
    /// was planted; whether the base was restored from the copy cache; each table's rows planted and skipped; each table that held a row
    /// as restored or took one, its rows and content hash as restored and after; and the copy's own answer to each of the change's
    /// preconditions, in the existing data's form. The three lists can be long.
    /// </summary>
    public static JsonObject SyntheticCopyContent => new()
    {
        ["syntheticCopy"] = Render.Record(new()
        {
            ["copy"] = Render.Pattern("^dbchange_synthetic_[0-9a-f]{16}_existing-data(_[0-9a-f]{16})?$"), ["at"] = Render.Text(),
            ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"), ["head"] = Render.Nullable(Render.Pattern("^[0-9a-f]{40,64}$")),
            ["restored"] = new JsonObject { ["type"] = "boolean" },
            ["planted"] = Render.Long(Render.Record(new() { ["table"] = Render.Text(), ["planted"] = Count(), ["skipped"] = Count() })),
            ["tables"] = Render.Long(Render.Record(new()
            {
                ["table"] = Render.Text(), ["rowsBefore"] = Count(), ["before"] = Render.Nullable(Render.Fingerprint()), ["rows"] = Count(),
                ["hash"] = Render.Fingerprint(),
            })),
            ["answers"] = Render.Long(Render.Record(new()
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
            })),
        }),
    };

    /// <summary>
    /// dbchange synthetic-copy up [--at &lt;ref&gt;] [--existing-data &lt;file&gt;] [--profile &lt;path&gt;] [--project &lt;path&gt;];
    /// check, status and down arrive later.
    /// </summary>
    public static Envelope SyntheticCopy(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words) => words switch
    {
        ["up", ..] => Up(here, log, [.. words.Skip(1)]),
        [var subcommand, ..] when LaterCopy.Contains(subcommand) => Contract.NotBuilt(Of("synthetic-copy") with
        {
            Name = "synthetic-copy " + subcommand,
        }) with { Schema = Of("synthetic-copy").Output },
        _ => Contract.Failed(Of("synthetic-copy"), new Error(
            "arguments.unknown-subcommand",
            "dbchange synthetic-copy needs what to do with the copy; this build runs synthetic-copy up.",
            "Run dbchange synthetic-copy up --at <ref>.")),
    };

    /// <summary>
    /// synthetic-copy up's arguments read, io/SyntheticCopy run, and its answer rendered: --at takes ref:&lt;ref&gt; or a ref alone, HEAD
    /// where it is absent; --existing-data a file by its path from the working directory.
    /// </summary>
    private static Envelope Up(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words)
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
}
