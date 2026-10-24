using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DbChangeLite;

public sealed record Outcome(string Word, int Exit, string Meaning);

public sealed record Flag(string Name, string Value, string Meaning, bool Required);

public sealed record Verb(string Name, string Summary, IReadOnlyList<Flag> Flags, IReadOnlyList<string> Switches, IReadOnlyList<Outcome> Outcomes);

public sealed record ExitCode(int Code, string Name, string Meaning, string Remedy);

public sealed record Invocation(Verb Verb, IReadOnlyDictionary<string, string> Values, IReadOnlySet<string> Switches);

public static class Contract
{
    public const string Version = "0.1.0";

    public const string StoppingRule =
        "Work every open item of the answer to closed, or to the person it names; stop when open is empty for every environment the change reaches.";

    public static readonly IReadOnlyList<ExitCode> Exits =
    [
        new(0, "done", "Done: the verb did its work.", "Take no action."),
        new(1, "bad-arguments", "Bad arguments: an unknown verb, flag or value.", "Run dbchange-lite --help to see the verbs and each one's flags."),
        new(2, "unparsed-input", "An input could not be parsed: a schema, a configuration file or a project.", "Correct the file at the line the finding names."),
        new(3, "blocked", "Blocked by the data, a finding and not a failure: blocked by BlockOnPossibleDataLoss, which stopped the publish because the"
            + " table has rows; or by a constraint violation, SQL Server refusing the change on existing rows (Msg 547, Msg 2628).",
            "Change the operation at the site the finding names: its two-release shape, or the rows it counts."),
        new(4, "unreachable", "The target is unreachable: no local server, or SQL Server or LocalDB not answering.",
            "Run dbchange-lite doctor, then start the local server or correct the environment's connection."),
        new(5, "differs", "Divergence found: the target differs from the repository; the findings name each differing object.",
            "Correct the target or the repository at the objects the findings name."),
        new(6, "configuration-refused", "The environment or configuration is refused: the .NET SDK missing, an unknown key, a literal credential, a DacFx release"
            + " outside the pinned window, a failure DacFx reports with no SQL Server error inside (dacfx.failed), or a defect in dbchange-lite itself (internal.unexpected).",
            "Run dbchange-lite doctor, or correct the file the finding names."),
        new(7, "build-failed", "The build failed; the findings carry the build's errors.", "Fix each error at the file and line the finding names, then build again."),
        new(8, "needs-input", "Needs input, the agent's next step, which each open item names: a command to run, a person to ask, or rows the copy needs.",
            "Do what each open item's next says, then run the verb again."),
        new(9, "refused-by-name", "Refused by name: a named environment, or a server that is not local; the refusal's code is printed.", "Do what the finding's remedy says."),
        new(130, "interrupted", "Interrupted (Ctrl-C or SIGTERM); the cleanup ran and the state is as before.", "Run the verb again."),
    ];

    private static readonly IReadOnlyDictionary<string, int> Words = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["done"] = 0, ["ready"] = 0, ["in-sync"] = 0, ["bad-arguments"] = 1, ["unparsed-input"] = 2, ["blocked"] = 3, ["unreachable"] = 4,
        ["differs"] = 5, ["degraded"] = 6, ["configuration-refused"] = 6, ["build-failed"] = 7, ["needs-input"] = 8, ["refused-by-name"] = 9,
        ["interrupted"] = 130,
    };

    private static readonly Flag Env = new("--env", "<name>", "An environment of dbchange/environments.json.", true);
    private static readonly Flag Source = new("--to", "<source>", "ref:<git ref>, dacpac:<path> or project:<path>; ref:HEAD when absent.", false);

    public static readonly IReadOnlyList<Verb> Verbs =
    [
        new("doctor", "Can this machine do the work? Each missing item with its remedy.", [], [],
            [new("ready", 0, "every item is present"), new("degraded", 6, "an item is missing; each finding names it and its remedy"),
             new("unreachable", 4, "an environment or the local server did not answer")]),
        new("drift", "Does the environment hold what the repository held at the release it was deployed from?",
            [Env, new("--at", "<source>", "The release the environment was deployed from: ref:<tag>, dacpac:<path> or project:<path>.", true)], [],
            [new("in-sync", 0, "the deploy plan against the environment is empty"),
             new("differs", 5, "the plan holds operations; a finding names each object, with its known cause where one applies"),
             new("unreachable", 4, "the environment did not answer")]),
        new("diff", "What does this branch change? One line per operation, as the corpus's change.txt writes them.",
            [new("--from", "<source>", "The source compared against.", true), new("--to", "<source>", "The source compared.", true)], [],
            [new("done", 0, "the operations are the findings"), new("build-failed", 7, "a source did not build")]),
        new("predict", "Will the change block on each environment, and why? Read-only; every open item names what closes it.",
            [new("--env", "<name>[,<name>]", "The environments to read, in deploy order.", true), Source,
             new("--onto", "<ref>|none", "The ref the change is merged onto before it is planned; ref:origin/main when absent.", false),
             new("--intent", "<operation>", "The operation the developer confirmed; a plan operation it cannot produce is an open item.", false),
             new("--at", "<source>", "The package an environment marked neverRead is planned against; --onto when absent.", false)],
            ["--counts"],
            [new("done", 0, "every environment read applies and no item is open"), new("blocked", 3, "an environment blocks"),
             new("needs-input", 8, "an item is open: a command to run, or a person to ask"), new("unreachable", 4, "no environment answered")]),
        new("prove", "What does a real deploy do on rows shaped like the environment's? The one write path, to a local copy only.",
            [Env, new("--rows", "<file>", "The rows the copy needs, as INSERT batches separated by GO.", true), Source,
             new("--onto", "<ref>|none", "The ref the change is merged onto before it is proved, as predict's; ref:origin/main when absent.", false),
             new("--at", "<source>", "The release the environment was deployed from, required for an environment never read.", false)], [],
            [new("done", 0, "the deploy applied"), new("blocked", 3, "the deploy was stopped by BlockOnPossibleDataLoss or a constraint violation"),
             new("needs-input", 8, "the copy's rows answer a check or a precondition otherwise than the environment's do"),
             new("refused-by-name", 9, "the local server is not local, or is a host the environments file names")]),
    ];

    public static Outcome Of(string word) => new(word, Words[word], Exits.First(e => e.Code == Words[word]).Meaning);

    public static Invocation Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            throw Bad("A verb comes first: " + string.Join(", ", Verbs.Select(v => v.Name)) + ", --help or --version.");
        }

        var verb = Verbs.FirstOrDefault(v => v.Name == args[0]) ?? throw Bad("There is no verb " + args[0] + ".");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var switches = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Count; i++)
        {
            var word = args[i];
            if (word == "--json")
            {
                continue;
            }

            if (verb.Switches.Contains(word))
            {
                switches.Add(word);
                continue;
            }

            var flag = verb.Flags.FirstOrDefault(f => f.Name == word) ?? throw Bad(verb.Name + " has no flag " + word + ".");
            if (i + 1 == args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw Bad(word + " needs a value " + flag.Value + ".");
            }

            if (!values.TryAdd(word, args[++i]))
            {
                throw Bad(word + " is given twice.");
            }
        }

        var missing = verb.Flags.Where(f => f.Required && !values.ContainsKey(f.Name)).Select(f => f.Name + " " + f.Value).ToList();
        return missing.Count == 0 ? new Invocation(verb, values, switches) : throw Bad(verb.Name + " needs " + string.Join(" and ", missing) + ".");
    }

    public static IReadOnlyList<(int Code, string Name)> Frozen(string folder) => File.ReadAllLines(Path.Combine(folder, "exits.frozen"))
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .Select(l => l.Split(' ', 2))
        .Select(p => (int.Parse(p[0], CultureInfo.InvariantCulture), p[1]))
        .ToList();

    public static string Help()
    {
        var lines = new List<string> { "dbchange-lite " + Version + ": predicts read-only whether a schema change blocks or applies on each environment, and proves it on a local copy.", "" };
        foreach (var verb in Verbs)
        {
            lines.Add("  dbchange-lite " + verb.Name + string.Concat(verb.Flags.Select(f => " " + (f.Required ? f.Name + " " + f.Value : "[" + f.Name + " " + f.Value + "]")))
                + string.Concat(verb.Switches.Select(s => " [" + s + "]")));
            lines.Add("      " + verb.Summary);
            lines.AddRange(verb.Outcomes.Select(o => "      " + o.Word + " (exit " + o.Exit.ToString(CultureInfo.InvariantCulture) + "): " + o.Meaning));
        }

        lines.Add("");
        lines.Add("Exits:");
        lines.AddRange(Exits.Select(e => "  " + e.Code.ToString(CultureInfo.InvariantCulture) + " " + e.Name + ": " + e.Meaning + " " + e.Remedy));
        lines.Add("");
        lines.Add(StoppingRule);
        return string.Join("\n", lines) + "\n";
    }

    public static object HelpAsData() => new
    {
        schema = "dbchange-lite.help/1",
        version = Version,
        stoppingRule = StoppingRule,
        verbs = Verbs.Select(v => new
        {
            name = v.Name,
            summary = v.Summary,
            flags = v.Flags.Select(f => new { name = f.Name, value = f.Value, meaning = f.Meaning, required = f.Required }),
            switches = v.Switches,
            outcomes = v.Outcomes.Select(o => new { word = o.Word, exit = o.Exit, meaning = o.Meaning }),
        }),
        exits = Exits.Select(e => new { code = e.Code, name = e.Name, meaning = e.Meaning, remedy = e.Remedy }),
    };

    private static Stop Bad(string message) =>
        new(Of("bad-arguments"), Finding.Error("arguments.invalid", "dbchange-lite", message, "Run dbchange-lite --help to see the verbs and each one's flags."));
}
