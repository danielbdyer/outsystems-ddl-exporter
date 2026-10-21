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
    /// <summary>
    /// What classify adds to the envelope: the ref and its commit, the environment planned against or null, the summary whose first word is
    /// provisional, each operation with its element and how it ships, each pre-deploy step, each unclassified entry, and the tables the CDC
    /// ledger names; the three lists can be long.
    /// </summary>
    public static JsonObject ClassifyContent => new()
    {
        ["classify"] = Render.Record(new()
        {
            ["at"] = Render.Text(), ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"), ["environment"] = Render.Nullable(Render.Text()),
            ["summary"] = Render.Pattern("^provisional: "),
            ["operations"] = Render.Long(Render.Record(new()
            {
                ["operation"] = Render.Text(), ["subject"] = Render.Text(), ["ships"] = Render.Text(),
            })),
            ["steps"] = Render.Long(Entry()), ["unclassified"] = Render.Long(Entry()), ["cdcTracked"] = Render.List(Render.Text()),
        }),
    };

    /// <summary>
    /// dbchange classify --at &lt;ref&gt; [--env &lt;environment&gt;] [--project &lt;path&gt;]: the change from the project at the ref
    /// to the project as the working tree holds it, classified provisionally (io/Classifier); --at takes ref:&lt;ref&gt; or a ref alone.
    /// </summary>
    public static Envelope Classify(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words)
    {
        var verb = Of("classify");
        if (Contract.Flags(words, ["--at"], ["--env", "--project"], [])
            .Bind(flags => GitRef.Of("--at",
                    flags["--at"].StartsWith("ref:", StringComparison.Ordinal) ? flags["--at"][4..] : flags["--at"])
                .Bind(at => (flags.TryGetValue("--env", out var env)
                        ? EnvironmentName.Of("--env", env).Map(name => (EnvironmentName?)name) : Result.Ok<EnvironmentName?>(null))
                    .Map(environment => new Classifier.Request(at, environment, flags.GetValueOrDefault("--project"),
                        Path.Combine(AppContext.BaseDirectory, "patterns")))))
            .Failed(out var request, out var error))
        {
            return Contract.Failed(verb, error, Standing.Committed);
        }

        var run = Classifier.Run(here, request, log);
        return run.Result.Failed(out var answer, out error) ? Contract.Failed(verb, error, run.Stamp) : Classified(answer, run.Stamp!);
    }

    /// <summary>
    /// classify's answer: refused, at the exit of its first refusal's category, when a finding refuses the change; else classified, exit
    /// 0. The message is the summary; the Markdown prints it first, then a line per operation, pre-deploy step and unclassified entry; the
    /// findings follow, the refusals with their remedies first. A refusal's exit requires a remedy of every finding, so a refused answer
    /// leaves out the notes the two models, the profile and the plan raised, which name no remedy; the answer once the refusal is resolved
    /// carries them.
    /// </summary>
    internal static Envelope Classified(Classifier.Answer answer, Stamp stamp)
    {
        var (verb, classified) = (Of("classify"), answer.Classified);
        var content = new JsonObject
        {
            ["classify"] = new JsonObject
            {
                ["at"] = "ref:" + answer.At, ["commit"] = answer.Commit, ["environment"] = answer.Environment?.ToString(),
                ["summary"] = classified.Summary,
                ["operations"] = Render.Array(classified.Operations.Select(o => new JsonObject
                {
                    ["operation"] = o.Operation, ["subject"] = o.Subject.ToString(), ["ships"] = o.Ships,
                })),
                ["steps"] = Render.Array(classified.Steps.Select(Entry)),
                ["unclassified"] = Render.Array(classified.Unclassified.Select(Entry)),
                ["cdcTracked"] = Render.Array(answer.Tracked.Select(t => (JsonNode?)t.ToString())),
            },
        };
        IReadOnlyList<string> lines =
        [
            classified.Summary,
            .. classified.Operations.Select(o => o.Operation + " " + o.Subject),
            .. classified.Steps.Select(s => "pre-deploy step " + s.Subject),
            .. classified.Unclassified.Select(u => "unclassified change: prove it, " + u.Kind.Word() + " " + u.Subject),
        ];
        var refusal = classified.Findings.FirstOrDefault(f => f.Severity == Severity.Error);
        var exit = refusal is null ? 0 : Contract.ExitByCategory(ErrorCode.Parse(refusal.Code.Split('.')[0])!.Value);
        return Contract.Answer(verb.Output, verb.Outcome(refusal is null ? "classified" : "refused"), exit, classified.Summary,
            [.. classified.Findings, .. answer.Notes.Where(n => refusal is null || n.Remedy is not null)], stamp, content: content,
            lines: lines);
    }

    private static JsonObject Entry() => Render.Record(new()
    {
        ["kind"] = Render.Enum(Enum.GetValues<ChangeKind>().Select(k => (JsonNode?)k.Word())), ["subject"] = Render.Text(),
    });

    private static JsonObject Entry(ChangeClassification.Entry entry) => new()
    {
        ["kind"] = entry.Kind.Word(), ["subject"] = entry.Subject.ToString(),
    };
}
