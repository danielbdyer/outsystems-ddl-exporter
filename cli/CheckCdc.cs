using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

public static partial class Verbs
{
    /// <summary>
    /// What check cdc adds to the envelope: the environment, the commit whose project was read, the date of the read, the counts of the
    /// tables declared and tracked, each table tracked, and the branch and commit --commit pushed, or null.
    /// </summary>
    internal static JsonObject CdcContent() => Render.Record(new()
    {
        ["environment"] = Render.Text(), ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"),
        ["measured"] = Render.Pattern("^[0-9]{4}-[0-9]{2}-[0-9]{2}$"),
        ["counts"] = Render.Record(new() { ["declared"] = Count(), ["tracked"] = Count() }),
        ["tracked"] = Render.Long(Render.Text()),
        ["branch"] = Render.Nullable(Render.Text()), ["pushed"] = Render.Nullable(Render.Pattern("^[0-9a-f]{40,64}$")),
    });

    /// <summary>
    /// dbchange check cdc --env &lt;environment&gt; [--project &lt;path&gt;] [--commit]: which of the tables the project declares at
    /// HEAD the environment's Change Data Capture tracks (io/CdcCheck); with --commit, dbchange/ledgers/cdc-tracked.md committed on a new
    /// branch and pushed for review.
    /// </summary>
    private static Envelope Cdc(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words)
    {
        var verb = Of("check");
        if (Contract.Flags(words, ["--env"], ["--project"], ["--commit"])
            .Bind(flags => EnvironmentName.Of("--env", flags["--env"]).Map(environment => (Flags: flags, Environment: environment)))
            .Failed(out var asked, out var error))
        {
            return Contract.Failed(verb, error, Standing.Committed);
        }

        var run = CdcCheck.Run(here, asked.Environment, asked.Flags.GetValueOrDefault("--project"), asked.Flags.ContainsKey("--commit"),
            log);
        return run.Result.Failed(out var answer, out error) ? Contract.Failed(verb, error, run.Stamp) : Cdc(answer, run.Stamp!);
    }

    /// <summary>
    /// check cdc's answer, at exit 0: listed, or proposed once --commit pushed the ledger. The message counts the tables tracked of those
    /// declared; the Markdown prints it, then the ledger's header and the environment's rows, one a table, as
    /// dbchange/ledgers/cdc-tracked.md holds them; no row for an environment that tracks none.
    /// </summary>
    internal static Envelope Cdc(CdcCheck.Answer answer, Stamp stamp)
    {
        var (verb, env) = (Of("check"), "env:" + answer.Environment);
        var rows = answer.Tracked.Select(t => new CdcLedger.Row(answer.Environment, t, answer.Measured).ToString()).ToList();
        var tracked = rows.Count == 0 ? "none" : rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var message = env + " tracks " + tracked + " of the "
            + Counted(answer.Declared, "table") + " the project declares at commit " + answer.Commit[..8] + " by Change Data Capture"
            + (answer.Pushed is { } pushed ? "; " + CdcLedger.File + " is pushed on " + pushed.Branch + ", for review." : ".");
        var content = new JsonObject
        {
            ["cdc"] = new JsonObject
            {
                ["environment"] = answer.Environment.ToString(), ["commit"] = answer.Commit, ["measured"] = answer.Measured,
                ["counts"] = new JsonObject { ["declared"] = answer.Declared, ["tracked"] = rows.Count },
                ["tracked"] = Render.Array(answer.Tracked.Select(t => (JsonNode?)t.ToString())),
                ["branch"] = answer.Pushed?.Branch, ["pushed"] = answer.Pushed?.Commit,
            },
        };
        return Contract.Answer(verb.Output, verb.Outcome(answer.Pushed is null ? "listed" : "proposed"), 0, message, answer.Notes, stamp,
            content: content, lines: rows.Count == 0 ? [] : [message, "", "| Environment | Table | Measured |", "|---|---|---|", .. rows]);
    }
}
