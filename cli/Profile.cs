using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

public static partial class Verbs
{
    /// <summary>
    /// What profile adds to the envelope: a column's shape, its environment, table, column and type, the summary's counts, its signatures
    /// with their rows, a list that can be long, and the fold; or a measurement's row tiers, its environment, its date, the ledger, each
    /// table's tier and whether its row was appended, and the branch and commit --commit pushed. One of the two is null.
    /// </summary>
    public static JsonObject ProfileContent => new()
    {
        ["profile"] = Render.Record(new()
        {
            ["environment"] = Render.Text(), ["table"] = Render.Text(), ["column"] = Render.Text(), ["type"] = Render.Text(),
            ["rows"] = Count(), ["nulls"] = Count(), ["empty"] = Count(), ["spacesOnly"] = Count(), ["edgeSpaces"] = Count(),
            ["maxLength"] = Render.Nullable(Count()), ["maxBytes"] = Render.Nullable(Count()), ["distinctAsCollated"] = Count(),
            ["distinctBinary"] = Render.Nullable(Count()),
            ["signatures"] = Render.Long(Render.Record(new() { ["signature"] = Render.Text(), ["rows"] = Count() })),
            ["folded"] = Render.Record(new() { ["rows"] = Count(), ["signatures"] = Count() }),
        }),
        ["rowTiers"] = Render.Record(new()
        {
            ["environment"] = Render.Text(), ["date"] = Render.Pattern("^[0-9]{4}-[0-9]{2}-[0-9]{2}$"), ["ledger"] = Render.Text(),
            ["tables"] = Render.Long(Render.Record(new()
            {
                ["table"] = Render.Text(), ["tier"] = Render.Nullable(Render.Enum(Io.Profile.TierNames.Select(t => (JsonNode?)t))),
                ["appended"] = new JsonObject { ["type"] = "boolean" },
            })),
            ["branch"] = Render.Nullable(Render.Text()), ["commit"] = Render.Nullable(Render.Pattern("^[0-9a-f]{40,64}$")),
        }),
    };

    /// <summary>
    /// dbchange profile --env &lt;environment&gt; &lt;schema.table.column&gt; [--vocabulary], or dbchange profile --env &lt;environment&gt;
    /// --counts [--commit]: the column, the one word outside a flag, is read here; io/Profile reads the rest.
    /// </summary>
    public static Envelope Profile(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words)
    {
        var verb = Of("profile");
        var (flagged, columns) = (new List<string>(), new List<string>());
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i] == "--env" && i + 1 < words.Count)
            {
                flagged.AddRange([words[i], words[++i]]);
            }
            else
            {
                (words[i].StartsWith("--", StringComparison.Ordinal) ? flagged : columns).Add(words[i]);
            }
        }

        if (Contract.Flags(flagged, ["--env"], [], ["--vocabulary", "--counts", "--commit"]).Bind(flags => Asked(flags, columns))
            .Failed(out var asked, out var error))
        {
            return Contract.Failed(verb, error);
        }

        return asked.Counts
            ? Io.Profile.Counts(here, asked.Environment, asked.Commit, log)
                .Match(tiers => Measured(tiers, asked.Commit), failed => Contract.Failed(verb, failed))
            : Io.Profile.Column(here, asked.Environment, asked.Column!, asked.Vocabulary, log)
                .Match(Shaped, failed => Contract.Failed(verb, failed));
    }

    /// <summary>What profile is asked: the environment, and the column with --vocabulary or not, or --counts with --commit or not; each
    /// other combination is a bad argument.</summary>
    private static Result<(string Environment, string? Column, bool Vocabulary, bool Counts, bool Commit)> Asked(
        IReadOnlyDictionary<string, string> flags, List<string> columns)
    {
        var (vocabulary, counts, commit) =
            (flags.ContainsKey("--vocabulary"), flags.ContainsKey("--counts"), flags.ContainsKey("--commit"));
        return counts && vocabulary ? Flag("--vocabulary", "a column's profile")
            : commit && !counts ? Flag("--commit", "profile --counts")
            : counts && columns.Count > 0 ? new Error("arguments.column",
                "dbchange profile --counts measures every table, and a column is named beside it.",
                "Run dbchange profile --env <environment> --counts, or name the column without --counts.")
            : !counts && columns.Count != 1 ? new Error("arguments.column",
                "dbchange profile reads one column, named as schema.table.column, or every table's rows with --counts, and "
                    + (columns.Count == 0 ? "neither is named." : "more than one column is named."),
                "Run dbchange profile --env <environment> <schema.table.column>, or dbchange profile --env <environment> --counts.")
            : (flags["--env"], counts ? null : columns[0], vocabulary, counts, commit);
    }

    private static Error Flag(string flag, string of) => new("arguments.unknown-flag", flag + " is a flag of " + of + " alone.",
        "Run dbchange --help to see each verb's flags.");

    /// <summary>A column's shape, done at exit 0: its counts in the message, the signatures and the fold in the content.</summary>
    private static Envelope Shaped(Io.Profile.Shape shape)
    {
        var verb = Of("profile");
        var (summary, signatures) = (shape.Summary, shape.Signatures);
        var named = shape.Named;
        var shownRows = signatures.Shown.Sum(s => s.Rows);
        var content = new JsonObject
        {
            ["profile"] = new JsonObject
            {
                ["environment"] = shape.Environment.ToString(), ["table"] = shape.Table.ToString(), ["column"] = shape.Column,
                ["type"] = shape.Type, ["rows"] = summary.Rows, ["nulls"] = summary.Nulls, ["empty"] = summary.Empty,
                ["spacesOnly"] = summary.SpacesOnly, ["edgeSpaces"] = summary.EdgeSpaces, ["maxLength"] = summary.MaxLength,
                ["maxBytes"] = summary.MaxBytes, ["distinctAsCollated"] = summary.DistinctAsCollated,
                ["distinctBinary"] = summary.DistinctBinary,
                ["signatures"] = Render.Array(signatures.Shown.Select(s => new JsonObject { ["signature"] = s.Text, ["rows"] = s.Rows })),
                ["folded"] = new JsonObject { ["rows"] = signatures.FoldedRows, ["signatures"] = signatures.FoldedSignatures },
            },
        };
        Finding[] notes = summary.DistinctBinary is null
            ? [Finding.Note("profile.distinct-binary", named, named + "'s collation has no binary collation of its code page that"
                + " dbchange writes, so its distinct values are counted as collated alone.")]
            : [];
        return Contract.Answer(verb.Output, verb.Outcome("done"), 0,
            "The shape of " + shape.Environment + "'s " + named + ", " + shape.Type + ": " + Counted(summary.Rows, "row") + ", "
                + Counted(summary.Nulls, "NULL") + " and " + Counted(summary.Empty, "empty string") + "; "
                + Counted(signatures.Shown.Count, "signature") + " seen " + SqlServer.Fold.ToString(CultureInfo.InvariantCulture)
                + " times or more hold " + Counted(shownRows, "row") + ", and " + Counted(signatures.FoldedRows, "row") + " in "
                + Counted(signatures.FoldedSignatures, "signature") + " are folded.",
            notes, content: content);
    }

    /// <summary>A measurement of row tiers at exit 0: measured, or pushed where --commit pushed the ledger; a note for each table whose
    /// partitions the login cannot see, and one where --commit had no row to push.</summary>
    private static Envelope Measured(Io.Profile.Tiers tiers, bool commit)
    {
        var verb = Of("profile");
        var date = tiers.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var appended = tiers.Tables.Count(t => t.Appended);
        var content = new JsonObject
        {
            ["rowTiers"] = new JsonObject
            {
                ["environment"] = tiers.Environment.ToString(), ["date"] = date, ["ledger"] = Io.Profile.Ledger,
                ["tables"] = Render.Array(tiers.Tables.Select(t => new JsonObject
                {
                    ["table"] = t.Table.ToString(), ["tier"] = t.Tier, ["appended"] = t.Appended,
                })),
                ["branch"] = tiers.Branch, ["commit"] = tiers.Commit,
            },
        };
        Finding[] notes =
        [
            .. tiers.Tables.Where(t => t.Tier is null).Select(t => Finding.Note("profile.unmeasured", t.Table.ToString(),
                "sys.partitions shows this login no partition of " + t.Table + ", so its tier is not measured.")),
            .. commit && tiers.Branch is null
                ? [Finding.Note("profile.unpushed", Io.Profile.Ledger,
                    "--commit pushed nothing, since the measurement appended no row to " + Io.Profile.Ledger + ".")]
                : Array.Empty<Finding>(),
        ];
        return Contract.Answer(verb.Output, verb.Outcome(tiers.Branch is null ? "measured" : "pushed"), 0,
            tiers.Environment + ": " + Counted(tiers.Tables.Count, "table") + " measured on " + date + "; " + Counted(appended, "row")
                + " appended to " + Io.Profile.Ledger + (tiers.Branch is { } branch ? ", pushed as " + branch : "") + ".",
            notes, content: content);
    }
}
